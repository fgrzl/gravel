using Gravel.Actor;
using Gravel.Actor.Messages;
using Microsoft.Extensions.Logging;

namespace Gravel.Actor.Tasks;

/// <summary>
///     Task for uploading a WAL segment to cloud storage.
/// </summary>
public sealed class UploadWalSegmentTask : IActorTask
{
    readonly Func<ValueTask> _uploadFunc;
    readonly ILogger? _logger;
    readonly string _taskName;
    int _retryCount;

    /// <summary>
    ///     Initializes a new instance of <see cref="UploadWalSegmentTask" />.
    /// </summary>
    /// <param name="sourceMessage">The upload message that triggered this task.</param>
    /// <param name="uploadFunc">Delegate to execute the upload.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public UploadWalSegmentTask(
        UploadWalSegmentMessage sourceMessage,
        Func<ValueTask> uploadFunc,
        ILogger? logger = null)
    {
        SourceMessage = sourceMessage;
        _uploadFunc = uploadFunc;
        _logger = logger;
        _taskName = $"UploadWAL({sourceMessage.LocalPath} -> {sourceMessage.RemotePath})";
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
    ///     Gets the estimated duration (null = unknown).
    /// </summary>
    public long? EstimatedDurationMs => null; // Depends on network conditions

    /// <summary>
    ///     Executes the WAL upload operation.
    /// </summary>
    public async ValueTask ExecuteAsync(CancellationToken ct = default)
    {
        _logger?.LogDebug("WAL upload task executing");
        await _uploadFunc().ConfigureAwait(false);
        _logger?.LogInformation("WAL upload task completed");
    }

    /// <summary>
    ///     Handles upload failures with retry logic.
    /// </summary>
    public async ValueTask<bool> HandleFailureAsync(Exception exception, CancellationToken ct = default)
    {
        _logger?.LogWarning(exception, "WAL upload failed (attempt {Attempt})", _retryCount + 1);
        _retryCount++;
        // Retry up to 5 times for transient network errors
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
