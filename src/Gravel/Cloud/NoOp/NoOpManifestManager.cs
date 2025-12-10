using Gravel.Cloud.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Cloud.NoOp;

/// <summary>
///     No-op implementation of manifest manager. Supports local-only deployments
///     where manifest is not persisted beyond runtime.
/// </summary>
public sealed class NoOpManifestManager : IManifestManager
{
    readonly ILogger _logger;
    ManifestData? _currentManifest;

    /// <summary>
    ///     Initializes a new instance of <see cref="NoOpManifestManager" />.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public NoOpManifestManager(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    ///     Loads the manifest (returns cached version or null).
    /// </summary>
    public ValueTask<ManifestData?> LoadAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Manifest load (no-op, returning null)");
        return ValueTask.FromResult(_currentManifest);
    }

    /// <summary>
    ///     Saves the manifest (caches in memory).
    /// </summary>
    public ValueTask SaveAsync(ManifestData manifest, CancellationToken ct = default)
    {
        _currentManifest = manifest;
        _logger.LogDebug("Manifest save (no-op): version={Version}", manifest.Version);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Logs a compaction action (no-op).
    /// </summary>
    public ValueTask LogCompactionActionAsync(CompactionAction action, CancellationToken ct = default)
    {
        _logger.LogDebug("Compaction action logged (no-op): {FromLevel}->{ToLevel}", action.FromLevel, action.ToLevel);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Gets compaction history (empty in no-op implementation).
    /// </summary>
    public ValueTask<IList<CompactionAction>> GetCompactionHistoryAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Compaction history requested (no-op, empty)");
        return ValueTask.FromResult<IList<CompactionAction>>(new List<CompactionAction>());
    }

    /// <summary>
    ///     Disposes the manager.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _logger.LogDebug("No-op manifest manager disposed");
        return ValueTask.CompletedTask;
    }
}
