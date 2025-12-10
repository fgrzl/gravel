namespace Gravel.Actor.Messages;

/// <summary>
///     Message signaling that an SST file should be evicted from the local cache.
///     The actor decides which files to evict and schedules eviction as a task.
/// </summary>
public sealed class EvictSstMessage : ActorMessage
{
    /// <summary>
    ///     The local path of the SST file to evict.
    /// </summary>
    public string SstPath { get; init; } = default!;

    /// <summary>
    ///     Size in bytes of the SST file (for space tracking).
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    ///     Whether the SST file should be pinned permanently (not evicted).
    /// </summary>
    public bool ShouldPin { get; init; }

    /// <summary>
    ///     Gets the source of this message.
    /// </summary>
    public override string Source => "sst_eviction";
}
