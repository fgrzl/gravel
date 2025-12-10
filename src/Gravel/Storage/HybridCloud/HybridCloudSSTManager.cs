using Gravel.Actor;
using Gravel.Actor.Messages;
using Gravel.Cloud.Abstractions;
using Gravel.Cloud.SST;
using Microsoft.Extensions.Logging;

namespace Gravel.Storage.HybridCloud;

/// <summary>
///     Hybrid cloud SST manager: cloud is source of truth, local NVMe is ephemeral cache.
///     - SSTs written directly to cloud
///     - Optional local cache for hot data
///     - Actor manages eviction when cache full
/// </summary>
public sealed class HybridCloudSSTManager : IAsyncDisposable
{
    readonly IActorRuntime _runtime;
    readonly ICloudStorage _cloudStorage;
    readonly string _localCacheDir;
    readonly long _maxCacheSizeBytes;
    readonly ILogger _logger;

    readonly Dictionary<string, CachedSST> _cache = new();
    long _cacheSizeBytes;

    /// <summary>
    ///     Initializes a new instance of <see cref="HybridCloudSSTManager" />.
    /// </summary>
    public HybridCloudSSTManager(
        IActorRuntime runtime,
        ICloudStorage cloudStorage,
        string localCacheDir,
        long maxCacheSizeBytes = 100 * 1024 * 1024, // 100 MB default
        ILogger? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _cloudStorage = cloudStorage ?? throw new ArgumentNullException(nameof(cloudStorage));
        _localCacheDir = localCacheDir ?? throw new ArgumentNullException(nameof(localCacheDir));
        _maxCacheSizeBytes = maxCacheSizeBytes;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        Directory.CreateDirectory(_localCacheDir);
    }

    /// <summary>
    ///     Writes SST directly to cloud.
    /// </summary>
    public async ValueTask<string> WriteAsync(
        IAsyncEnumerable<(byte Type, System.ReadOnlyMemory<byte> Key, System.ReadOnlyMemory<byte> Value, ulong Sequence)> entries,
        string? fileName = null,
        System.Threading.CancellationToken ct = default)
    {
        fileName ??= $"sst-{DateTime.UtcNow.Ticks:D20}.sst";
        var cloudPath = $"sst/{fileName}";

        // Write to temporary local file first
        var tempFile = Path.Combine(_localCacheDir, $".{fileName}.tmp");
        await using (var fileStream = System.IO.File.Create(tempFile))
        {
            await using var writer = new CloudNativeSSTWriter(fileStream, sparseIndexInterval: 64);
            await writer.WriteEntriesAsync(entries, ct).ConfigureAwait(false);
        }

        // Upload to cloud
        using (var fileStream = System.IO.File.OpenRead(tempFile))
        {
            await _cloudStorage.UploadAsync(cloudPath, fileStream, null, ct).ConfigureAwait(false);
        }

        // Optionally keep in local cache
        var fileInfo = new System.IO.FileInfo(tempFile);
        MaybeCacheFile(cloudPath, tempFile, fileInfo.Length);

        _logger.LogDebug("SST written to cloud: {CloudPath}, {Size} bytes", cloudPath, fileInfo.Length);

        return cloudPath;
    }

    /// <summary>
    ///     Reads SST (from cache if available, else from cloud).
    /// </summary>
    public async ValueTask<CloudNativeSSTReader> ReadAsync(string cloudPath, System.Threading.CancellationToken ct = default)
    {
        // Check cache first
        lock (_cache)
        {
            if (_cache.TryGetValue(cloudPath, out var cached))
            {
                cached.LastAccessedUtc = DateTime.UtcNow;
                _logger.LogDebug("SST cache hit: {CloudPath}", cloudPath);
                return new CloudNativeSSTReader(cached.Data);
            }
        }

        // Download from cloud
        _logger.LogDebug("SST cache miss, downloading: {CloudPath}", cloudPath);
        using var ms = new MemoryStream();
        await _cloudStorage.DownloadAsync(cloudPath, ms, ct).ConfigureAwait(false);

        var data = ms.ToArray();

        // Cache it if space available
        MaybeEvictAndCache(cloudPath, data);

        return new CloudNativeSSTReader(data);
    }

    /// <summary>
    ///     Pins an SST to prevent eviction.
    /// </summary>
    public async ValueTask PinAsync(string cloudPath, System.Threading.CancellationToken ct = default)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(cloudPath, out var cached))
            {
                cached.IsPinned = true;
                _logger.LogDebug("SST pinned: {CloudPath}", cloudPath);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Unpins an SST, allowing eviction.
    /// </summary>
    public async ValueTask UnpinAsync(string cloudPath, System.Threading.CancellationToken ct = default)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(cloudPath, out var cached))
            {
                cached.IsPinned = false;
                _logger.LogDebug("SST unpinned: {CloudPath}", cloudPath);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Prefetches SST to cache.
    /// </summary>
    public async ValueTask PrefetchAsync(string cloudPath, System.Threading.CancellationToken ct = default)
    {
        if (!_cache.ContainsKey(cloudPath))
        {
            _ = await ReadAsync(cloudPath, ct).ConfigureAwait(false);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Gets cache statistics.
    /// </summary>
    public HybridCloudSSTStats GetStats()
    {
        lock (_cache)
        {
            return new HybridCloudSSTStats
            {
                CachedSizeBytes = _cacheSizeBytes,
                MaxCacheSizeBytes = _maxCacheSizeBytes,
                CachedFileCount = _cache.Count,
                CacheHits = 0, // Would be tracked separately
                CacheMisses = 0,
                EvictionCount = 0
            };
        }
    }

    private void MaybeCacheFile(string cloudPath, string localPath, long size)
    {
        lock (_cache)
        {
            if (_cacheSizeBytes + size <= _maxCacheSizeBytes)
            {
                var data = System.IO.File.ReadAllBytes(localPath);
                _cache[cloudPath] = new CachedSST
                {
                    CloudPath = cloudPath,
                    LocalPath = localPath,
                    Data = data,
                    SizeBytes = size,
                    CreatedAtUtc = DateTime.UtcNow,
                    LastAccessedUtc = DateTime.UtcNow,
                    IsPinned = false
                };
                _cacheSizeBytes += size;
                _logger.LogDebug("SST cached: {CloudPath}, cache size now {TotalSize} bytes", cloudPath, _cacheSizeBytes);
            }
            else
            {
                // Delete local copy if not caching
                try
                {
                    System.IO.File.Delete(localPath);
                }
                catch { }
            }
        }
    }

    private void MaybeEvictAndCache(string cloudPath, byte[] data)
    {
        lock (_cache)
        {
            var size = data.Length;

            // Evict LRU items until space available
            while (_cacheSizeBytes + size > _maxCacheSizeBytes && _cache.Count > 0)
            {
                var lruKey = _cache
                    .Where(kvp => !kvp.Value.IsPinned)
                    .OrderBy(kvp => kvp.Value.LastAccessedUtc)
                    .FirstOrDefault()
                    .Key;

                if (lruKey != null && _cache.TryGetValue(lruKey, out var lru))
                {
                    _cacheSizeBytes -= lru.SizeBytes;
                    _cache.Remove(lruKey);

                    try
                    {
                        System.IO.File.Delete(lru.LocalPath);
                    }
                    catch { }

                    _logger.LogDebug("Evicted SST: {CloudPath}", lruKey);
                }
                else
                {
                    break;
                }
            }

            // Cache the new file
            if (_cacheSizeBytes + size <= _maxCacheSizeBytes)
            {
                var tempPath = Path.Combine(_localCacheDir, Path.GetFileName(cloudPath));
                _cache[cloudPath] = new CachedSST
                {
                    CloudPath = cloudPath,
                    LocalPath = tempPath,
                    Data = data,
                    SizeBytes = size,
                    CreatedAtUtc = DateTime.UtcNow,
                    LastAccessedUtc = DateTime.UtcNow,
                    IsPinned = false
                };
                _cacheSizeBytes += size;
                _logger.LogDebug("SST cached: {CloudPath}, cache size now {TotalSize} bytes", cloudPath, _cacheSizeBytes);
            }
        }
    }

    /// <summary>
    ///     Disposes the manager.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        lock (_cache)
        {
            _cache.Clear();
        }

        await Task.CompletedTask;
    }

    sealed class CachedSST
    {
        public required string CloudPath { get; init; }
        public required string LocalPath { get; init; }
        public required byte[] Data { get; init; }
        public required long SizeBytes { get; init; }
        public required DateTime CreatedAtUtc { get; init; }
        public DateTime LastAccessedUtc { get; set; }
        public bool IsPinned { get; set; }
    }
}

/// <summary>
///     Statistics about hybrid cloud SST cache.
/// </summary>
public sealed class HybridCloudSSTStats
{
    /// <summary>
    ///     Current cache size in bytes.
    /// </summary>
    public long CachedSizeBytes { get; init; }

    /// <summary>
    ///     Maximum cache size in bytes.
    /// </summary>
    public long MaxCacheSizeBytes { get; init; }

    /// <summary>
    ///     Number of cached files.
    /// </summary>
    public int CachedFileCount { get; init; }

    /// <summary>
    ///     Cache hits (informational).
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    ///     Cache misses (informational).
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    ///     Number of evictions.
    /// </summary>
    public long EvictionCount { get; init; }
}
