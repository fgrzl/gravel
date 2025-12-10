namespace Gravel.Cloud.Abstractions;

/// <summary>
///     Cloud-native SST (Sorted String Table) manager that treats SST files as primarily
///     living in cloud storage with local NVMe as an optional cache layer.
///     The actor decides what to pin, evict, and prefetch.
/// </summary>
public interface ICloudSstManager : IAsyncDisposable
{
    /// <summary>
    ///     Writes a new SST file directly to cloud storage.
    ///     Optionally caches it locally if in-demand.
    /// </summary>
    /// <param name="sstData">The SST file data to upload.</param>
    /// <param name="metadata">Optional metadata (level, sequence range, etc.).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Path to the cloud-stored SST file.</returns>
    ValueTask<string> WriteAsync(
        Stream sstData,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Reads an SST file, preferring local cache but falling back to cloud if needed.
    /// </summary>
    /// <param name="path">The SST path (cloud or cached local).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A stream to the SST data.</returns>
    ValueTask<Stream> ReadAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Pins an SST file to the local cache (prevents eviction).
    /// </summary>
    /// <param name="path">The SST path to pin.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask PinAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Unpins an SST file, allowing it to be evicted if needed.
    /// </summary>
    /// <param name="path">The SST path to unpin.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask UnpinAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Prefetches an SST file to the local cache asynchronously.
    /// </summary>
    /// <param name="path">The SST path to prefetch.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask PrefetchAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Evicts an SST file from the local cache (keeps it in cloud).
    /// </summary>
    /// <param name="path">The SST path to evict.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask EvictAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Gets current cache statistics.
    /// </summary>
    SstCacheStats GetCacheStats();

    /// <summary>
    ///     Deletes an SST file from both local cache and cloud storage.
    /// </summary>
    /// <param name="path">The SST path to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask DeleteAsync(string path, CancellationToken ct = default);
}

/// <summary>
///     Statistics about the SST cache layer.
/// </summary>
public sealed class SstCacheStats
{
    /// <summary>
    ///     Total size of cached SST files in bytes.
    /// </summary>
    public long CachedSizeBytes { get; init; }

    /// <summary>
    ///     Maximum size allowed for the cache in bytes.
    /// </summary>
    public long MaxCacheSizeBytes { get; init; }

    /// <summary>
    ///     Number of SST files currently cached.
    /// </summary>
    public int CachedFileCount { get; init; }

    /// <summary>
    ///     Number of cache hits (reads that found the file locally).
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    ///     Number of cache misses (reads that had to fetch from cloud).
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    ///     Number of files that were evicted due to space constraints.
    /// </summary>
    public long EvictionCount { get; init; }
}
