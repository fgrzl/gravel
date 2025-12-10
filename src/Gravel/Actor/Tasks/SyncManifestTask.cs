using Gravel.Actor;
using Gravel.Actor.Messages;
using Gravel.Cloud.Abstractions;
using Microsoft.Extensions.Logging;

namespace Gravel.Actor.Tasks;

/// <summary>
///     Task for syncing the manifest to persistent storage.
/// </summary>
public sealed class SyncManifestTask : IActorTask
{
    readonly IManifestManager _manifestManager;
    readonly ManifestData _manifestData;
    readonly ILogger? _logger;
    readonly string _taskName;
    int _retryCount;

    /// <summary>
    ///     Initializes a new instance of <see cref="SyncManifestTask" />.
    /// </summary>
    /// <param name="sourceMessage">The sync message that triggered this task.</param>
    /// <param name="manifestData">The manifest data to persist.</param>
    /// <param name="manifestManager">The manifest manager to use for persistence.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public SyncManifestTask(
        SyncManifestMessage sourceMessage,
        ManifestData manifestData,
        IManifestManager manifestManager,
        ILogger? logger = null)
    {
        SourceMessage = sourceMessage;
        _manifestData = manifestData;
        _manifestManager = manifestManager;
        _logger = logger;
        _taskName = $"SyncManifest(version={sourceMessage.ManifestVersion})";
    }

    /// <summary>
    ///     Gets the unique task identifier.
    /// </summary>
    public Guid TaskId { get; } = Guid.NewGuid();

    /// <summary>
    ///     Gets the source message that triggered this task.
    /// </summary>
    public ActorMessage SourceMessage { get; }

    /// <summary>
    ///     Gets the descriptive name of this task.
    /// </summary>
    public string TaskName => _taskName;

    /// <summary>
    ///     Gets the estimated duration (usually quick).
    /// </summary>
    public long? EstimatedDurationMs => 100; // Usually quick

    /// <summary>
    ///     Executes the manifest sync operation.
    /// </summary>
    public async ValueTask ExecuteAsync(CancellationToken ct = default)
    {
        _logger?.LogDebug("Manifest sync task executing");
        await _manifestManager.SaveAsync(_manifestData, ct).ConfigureAwait(false);
        _logger?.LogInformation("Manifest sync task completed: version={Version}", _manifestData.Version);
    }

    /// <summary>
    ///     Handles sync failures with retry logic.
    /// </summary>
    public async ValueTask<bool> HandleFailureAsync(Exception exception, CancellationToken ct = default)
    {
        _logger?.LogWarning(exception, "Manifest sync failed (attempt {Attempt})", _retryCount + 1);
        _retryCount++;
        // Retry up to 5 times for manifest persistence
        return _retryCount < 5;
    }

    /// <summary>
    ///     Disposes the task.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
