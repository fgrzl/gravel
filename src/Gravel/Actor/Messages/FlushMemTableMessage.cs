namespace Gravel.Actor.Messages;

/// <summary>
///     Message signaling that a memtable should be flushed to SST.
/// </summary>
public sealed class FlushMemTableMessage : ActorMessage
{
    /// <summary>
    ///     The sequence number up to which the memtable contains data (for sequencing).
    /// </summary>
    public ulong UpToSequence { get; init; }

    /// <summary>
    ///     Number of entries in the memtable being flushed.
    /// </summary>
    public int EntryCount { get; init; }

    /// <summary>
    ///     Gets the source of this message.
    /// </summary>
    public override string Source => "flush";
}
