namespace Gravel.Actor;

/// <summary>
///     Intent log entry recording an action the actor decided to take.
///     Used for debugging, recovery, and replaying compaction decisions deterministically.
/// </summary>
public sealed class ActorIntentLogEntry
{
    /// <summary>
    ///     When this intent was logged (UTC ticks).
    /// </summary>
    public long CreatedAtTicks { get; init; } = DateTime.UtcNow.Ticks;

    /// <summary>
    ///     The source message that led to this intent.
    /// </summary>
    public ActorMessage SourceMessage { get; init; } = default!;

    /// <summary>
    ///     The task that was scheduled as a result of this intent.
    /// </summary>
    public IActorTask ScheduledTask { get; init; } = default!;

    /// <summary>
    ///     Structured metadata about the intent (e.g., which levels were compacted, file counts).
    /// </summary>
    public Dictionary<string, object?> Metadata { get; init; } = [];

    /// <summary>
    ///     Status of the intent (scheduled, executing, completed, failed).
    /// </summary>
    public ActorIntentStatus Status { get; set; } = ActorIntentStatus.Scheduled;

    /// <summary>
    ///     If the intent failed, the error message.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
///     Status of an intent log entry.
/// </summary>
public enum ActorIntentStatus
{
    /// <summary>
    ///     Intent has been scheduled but not yet executing.
    /// </summary>
    Scheduled,

    /// <summary>
    ///     Intent is currently being executed.
    /// </summary>
    Executing,

    /// <summary>
    ///     Intent completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    ///     Intent failed (possibly with retry pending).
    /// </summary>
    Failed,

    /// <summary>
    ///     Intent was cancelled.
    /// </summary>
    Cancelled
}
