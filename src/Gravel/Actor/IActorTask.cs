namespace Gravel.Actor;

/// <summary>
///     Represents a single piece of work that the actor can dispatch to background tasks.
///     All tasks are executed via the actor, ensuring deterministic ordering.
/// </summary>
public interface IActorTask : IAsyncDisposable
{
    /// <summary>
    ///     Unique identifier for this task.
    /// </summary>
    Guid TaskId { get; }

    /// <summary>
    ///     The message that triggered creation of this task.
    /// </summary>
    ActorMessage SourceMessage { get; }

    /// <summary>
    ///     Descriptive name for logging and monitoring.
    /// </summary>
    string TaskName { get; }

    /// <summary>
    ///     Optional duration estimate (in milliseconds) for scheduling/throttling purposes.
    /// </summary>
    long? EstimatedDurationMs { get; }

    /// <summary>
    ///     Executes the task. Must be idempotent (safe to retry on transient failures).
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the execution.</returns>
    ValueTask ExecuteAsync(CancellationToken ct = default);

    /// <summary>
    ///     Called if the task fails. Allows the task to log, clean up, or schedule retries.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True to retry the task; false to abandon it.</returns>
    ValueTask<bool> HandleFailureAsync(Exception exception, CancellationToken ct = default)
    {
        return ValueTask.FromResult(false);
    }

    /// <summary>
    ///     Called when the task completes successfully.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the completion callback.</returns>
    ValueTask OnCompleteAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }
}
