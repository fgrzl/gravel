using Gravel.Cloud.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Cloud.NoOp;

/// <summary>
///     No-op implementation of cloud SST manager. Supports local-only deployments.
///     All cloud operations complete successfully but don't persist to external storage.
/// </summary>
public sealed class NoOpCloudSstManager : ICloudSstManager
{
    readonly ILogger _logger;

    /// <summary>
    ///     Initializes a new instance of <see cref="NoOpCloudSstManager" />.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public NoOpCloudSstManager(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    ///     Writes SST data (returns a cloud path but doesn't persist).
    /// </summary>
    public ValueTask<string> WriteAsync(
        Stream sstData,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var path = $"cloud://sst/{Guid.NewGuid():N}";
        _logger.LogDebug("SST write: {Path}", path);
        return ValueTask.FromResult(path);
    }

    /// <summary>
    ///     Reads SST data (always fails in no-op implementation).
    /// </summary>
    public ValueTask<Stream> ReadAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("SST read attempted: {Path} (no-op, will fail)", path);
        throw new FileNotFoundException($"No-op SST manager has no file: {path}");
    }

    /// <summary>
    ///     Pins an SST file (no-op).
    /// </summary>
    public ValueTask PinAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("SST pin: {Path}", path);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Unpins an SST file (no-op).
    /// </summary>
    public ValueTask UnpinAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("SST unpin: {Path}", path);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Prefetches an SST file (no-op).
    /// </summary>
    public ValueTask PrefetchAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("SST prefetch: {Path}", path);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Evicts an SST file (no-op).
    /// </summary>
    public ValueTask EvictAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("SST evict: {Path}", path);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Gets cache statistics (all zeros in no-op implementation).
    /// </summary>
    public SstCacheStats GetCacheStats()
    {
        return new SstCacheStats
        {
            CachedSizeBytes = 0,
            MaxCacheSizeBytes = 0,
            CachedFileCount = 0,
            CacheHits = 0,
            CacheMisses = 0,
            EvictionCount = 0
        };
    }

    /// <summary>
    ///     Deletes an SST file (no-op).
    /// </summary>
    public ValueTask DeleteAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("SST delete: {Path}", path);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Disposes the manager.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _logger.LogDebug("No-op cloud SST manager disposed");
        return ValueTask.CompletedTask;
    }
}
