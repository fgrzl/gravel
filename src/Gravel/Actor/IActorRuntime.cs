namespace Gravel.Actor;

/// <summary>
///     Core actor runtime that sequences all background operations (flush, compaction, WAL upload, manifest sync, eviction).
///     Ensures deterministic ordering and predictable state transitions.
/// </summary>
public interface IActorRuntime : IAsyncDisposable
{
    /// <summary>
    ///     Posts a message to the actor queue for processing.
    ///     Returns immediately; message is processed asynchronously.
    /// </summary>
    /// <param name="message">The message to post.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the enqueue operation.</returns>
    ValueTask PostMessageAsync(ActorMessage message, CancellationToken ct = default);

    /// <summary>
    ///     Enqueues a task for execution. The actor will execute tasks in strict order
    ///     according to their source message sequence.
    /// </summary>
    /// <param name="task">The task to enqueue.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the enqueue operation.</returns>
    ValueTask EnqueueTaskAsync(IActorTask task, CancellationToken ct = default);

    /// <summary>
    ///     Synchronously returns the current intent log (for debugging/recovery).
    ///     The log is a read-only snapshot; modifications should go through the actor.
    /// </summary>
    /// <returns>A read-only list of intent log entries.</returns>
    IReadOnlyList<ActorIntentLogEntry> GetIntentLog();

    /// <summary>
    ///     Waits for all enqueued tasks to complete. Useful for testing and graceful shutdown.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the wait operation.</returns>
    ValueTask WaitForQuiesceAsync(CancellationToken ct = default);

    /// <summary>
    ///     Returns statistics about actor runtime performance.
    /// </summary>
    ActorRuntimeStats GetStats();
}

/// <summary>
///     Statistics about actor runtime behavior.
/// </summary>
public sealed class ActorRuntimeStats
{
    /// <summary>
    ///     Total number of messages processed by the actor.
    /// </summary>
    public long MessagesProcessed { get; init; }

    /// <summary>
    ///     Total number of tasks enqueued and executed.
    /// </summary>
    public long TasksCompleted { get; init; }

    /// <summary>
    ///     Total number of task failures (before retry/abandonment).
    /// </summary>
    public long TaskFailures { get; init; }

    /// <summary>
    ///     Current number of tasks waiting to be executed.
    /// </summary>
    public int PendingTasks { get; init; }

    /// <summary>
    ///     Total duration spent executing tasks (in milliseconds).
    /// </summary>
    public long TotalExecutionMs { get; init; }

    /// <summary>
    ///     Average time per task (in milliseconds).
    /// </summary>
    public double AverageTaskMs =>
        TasksCompleted > 0 ? (double)TotalExecutionMs / TasksCompleted : 0;
}
