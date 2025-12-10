namespace Gravel.Actor;

/// <summary>
///     Base class for all actor messages. The runtime dispatches these in strict sequence order,
///     ensuring determinism and predictable state transitions.
/// </summary>
public abstract class ActorMessage
{
    /// <summary>
    ///     Unique message identifier for tracking and deduplication.
    /// </summary>
    public Guid MessageId { get; } = Guid.NewGuid();

    /// <summary>
    ///     When this message was created (UTC ticks).
    /// </summary>
    public long CreatedAtTicks { get; } = DateTime.UtcNow.Ticks;

    /// <summary>
    ///     Source that originated this message (e.g., "write_path", "compaction", "wal_upload").
    /// </summary>
    public abstract string Source { get; }
}
