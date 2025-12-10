using Gravel.Actor;
using Gravel.Cloud.Abstractions;
using Gravel.Storage.HybridCloud;
using Gravel.Storage.Local;
using Microsoft.Extensions.Logging;

namespace Gravel.Storage;

/// <summary>
///     Storage mode selection.
/// </summary>
public enum StorageMode
{
    /// <summary>
    ///     Local filesystem only - all data on disk.
    /// </summary>
    LocalOnly,

    /// <summary>
    ///     Hybrid cloud - cloud is source of truth, local is ephemeral cache.
    /// </summary>
    HybridCloud
}

/// <summary>
///     Configuration for storage layer.
/// </summary>
public sealed class StorageConfig
{
    /// <summary>
    ///     Which storage mode to use.
    /// </summary>
    public required StorageMode Mode { get; init; }

    /// <summary>
    ///     Local path for WAL/SST/cache (required for all modes).
    /// </summary>
    public required string LocalPath { get; init; }

    /// <summary>
    ///     Cloud storage implementation (required for HybridCloud mode).
    /// </summary>
    public ICloudStorage? CloudStorage { get; init; }

    /// <summary>
    ///     Cloud bucket/container name (required for HybridCloud mode).
    /// </summary>
    public string? CloudBucket { get; init; }

    /// <summary>
    ///     Max local cache size in bytes (HybridCloud only).
    /// </summary>
    public long LocalCacheSizeBytes { get; init; } = 100 * 1024 * 1024; // 100 MB default

    /// <summary>
    ///     WAL segment size threshold for rolling.
    /// </summary>
    public int WalSegmentSizeBytes { get; init; } = 10 * 1024 * 1024; // 10 MB default
}

/// <summary>
///     Factory for creating storage implementations.
/// </summary>
public static class StorageFactory
{
    /// <summary>
    ///     Creates a storage implementation based on configuration.
    /// </summary>
    public static StorageInstance Create(StorageConfig config, IActorRuntime runtime, ILogger? logger = null)
    {
        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        return config.Mode switch
        {
            StorageMode.LocalOnly => CreateLocalOnly(config, logger),
            StorageMode.HybridCloud => CreateHybridCloud(config, runtime, logger),
            _ => throw new ArgumentException($"Unknown storage mode: {config.Mode}")
        };
    }

    private static StorageInstance CreateLocalOnly(StorageConfig config, ILogger logger)
    {
        var walDir = Path.Combine(config.LocalPath, "wal");
        var sstDir = Path.Combine(config.LocalPath, "sst");

        var wal = new LocalWAL(walDir, config.WalSegmentSizeBytes, logger);
        var sst = new LocalSSTManager(sstDir, logger);

        logger.LogInformation("Storage mode: LocalOnly at {Path}", config.LocalPath);

        return new StorageInstance
        {
            Mode = StorageMode.LocalOnly,
            LocalWAL = wal,
            LocalSST = sst,
            HybridWAL = null,
            HybridSST = null
        };
    }

    private static StorageInstance CreateHybridCloud(StorageConfig config, IActorRuntime runtime, ILogger logger)
    {
        if (config.CloudStorage == null)
            throw new ArgumentException("CloudStorage is required for HybridCloud mode");

        var cacheDir = Path.Combine(config.LocalPath, "cache");
        var wal = new HybridCloudWAL(runtime, config.CloudStorage, cacheDir, config.WalSegmentSizeBytes, logger);
        var sst = new HybridCloudSSTManager(runtime, config.CloudStorage, cacheDir, config.LocalCacheSizeBytes, logger);

        logger.LogInformation(
            "Storage mode: HybridCloud, local cache at {Path}, max size {Size} bytes",
            cacheDir,
            config.LocalCacheSizeBytes);

        return new StorageInstance
        {
            Mode = StorageMode.HybridCloud,
            LocalWAL = null,
            LocalSST = null,
            HybridWAL = wal,
            HybridSST = sst
        };
    }
}

/// <summary>
///     Storage instance containing the configured implementations.
/// </summary>
public sealed class StorageInstance : IAsyncDisposable
{
    /// <summary>
    ///     Storage mode being used.
    /// </summary>
    public required StorageMode Mode { get; init; }

    /// <summary>
    ///     Local WAL instance (LocalOnly mode).
    /// </summary>
    public LocalWAL? LocalWAL { get; init; }

    /// <summary>
    ///     Local SST manager (LocalOnly mode).
    /// </summary>
    public LocalSSTManager? LocalSST { get; init; }

    /// <summary>
    ///     Hybrid cloud WAL instance (HybridCloud mode).
    /// </summary>
    public HybridCloudWAL? HybridWAL { get; init; }

    /// <summary>
    ///     Hybrid cloud SST manager (HybridCloud mode).
    /// </summary>
    public HybridCloudSSTManager? HybridSST { get; init; }

    /// <summary>
    ///     Gets the WAL for the current mode.
    /// </summary>
    public object GetWAL()
    {
        return Mode switch
        {
            StorageMode.LocalOnly => LocalWAL ?? throw new InvalidOperationException("LocalWAL not initialized"),
            StorageMode.HybridCloud => HybridWAL ?? throw new InvalidOperationException("HybridWAL not initialized"),
            _ => throw new InvalidOperationException($"Unknown storage mode: {Mode}")
        };
    }

    /// <summary>
    ///     Gets the SST manager for the current mode.
    /// </summary>
    public object GetSSTManager()
    {
        return Mode switch
        {
            StorageMode.LocalOnly => LocalSST ?? throw new InvalidOperationException("LocalSST not initialized"),
            StorageMode.HybridCloud => HybridSST ?? throw new InvalidOperationException("HybridSST not initialized"),
            _ => throw new InvalidOperationException($"Unknown storage mode: {Mode}")
        };
    }

    /// <summary>
    ///     Disposes all storage resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (LocalWAL != null) await LocalWAL.DisposeAsync().ConfigureAwait(false);
        if (LocalSST != null) await LocalSST.DisposeAsync().ConfigureAwait(false);
        if (HybridWAL != null) await HybridWAL.DisposeAsync().ConfigureAwait(false);
        if (HybridSST != null) await HybridSST.DisposeAsync().ConfigureAwait(false);
    }
}
