namespace Gravel.Actor.Messages;

/// <summary>
///     Message signaling that a compaction pass should be scheduled.
///     The actor decides which levels are ready and enqueues appropriate compaction tasks.
/// </summary>
public sealed class ScheduleCompactionMessage : ActorMessage
{
    /// <summary>
    ///     Optionally force compaction at a specific level (0-based). If -1, auto-detect.
    /// </summary>
    public int? ForceLevelIndex { get; init; }

    /// <summary>
    ///     Whether this is a manual compaction request (vs. automatic background trigger).
    /// </summary>
    public bool IsManual { get; init; }

    /// <summary>
    ///     Gets the source of this message.
    /// </summary>
    public override string Source => "compaction_schedule";
}
