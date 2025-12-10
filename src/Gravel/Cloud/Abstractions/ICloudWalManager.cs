using Gravel.Abstractions;

namespace Gravel.Cloud.Abstractions;

/// <summary>
///     Cloud-native WAL (Write-Ahead Log) abstraction that decouples local buffering
///     from cloud durability. Supports local fast appends with background cloud uploads.
/// </summary>
public interface ICloudWalManager : IAsyncDisposable
{
    /// <summary>
    ///     The current sequence number of the last durable write.
    /// </summary>
    ulong LastDurableSequence { get; }

    /// <summary>
    ///     Appends an entry to the local WAL buffer. Returns immediately; cloud upload happens asynchronously.
    /// </summary>
    /// <param name="entry">The entry to append.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask AppendAsync(DbEntry entry, CancellationToken ct = default);

    /// <summary>
    ///     Flushes the current WAL segment to cloud storage asynchronously.
    ///     Returns a task representing the cloud upload, but doesn't block the caller.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the background upload operation.</returns>
    ValueTask FlushSegmentAsync(CancellationToken ct = default);

    /// <summary>
    ///     Recovers the database state from cloud WAL segments and local manifest.
    ///     Called during initialization.
    /// </summary>
    /// <param name="onEntry">Callback invoked for each recovered entry.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RecoverAsync(Func<DbEntry, ValueTask> onEntry, CancellationToken ct = default);

    /// <summary>
    ///     Gets the list of all WAL segments (local and cloud) in chronological order.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of segment metadata.</returns>
    ValueTask<IList<WalSegmentInfo>> GetSegmentsAsync(CancellationToken ct = default);
}

/// <summary>
///     Metadata about a WAL segment.
/// </summary>
public sealed class WalSegmentInfo
{
    /// <summary>
    ///     Identifier of the segment (usually timestamp or sequence-based).
    /// </summary>
    public string SegmentId { get; init; } = default!;

    /// <summary>
    ///     Starting sequence number of entries in this segment.
    /// </summary>
    public ulong StartSequence { get; init; }

    /// <summary>
    ///     Ending sequence number of entries in this segment (inclusive).
    /// </summary>
    public ulong EndSequence { get; init; }

    /// <summary>
    ///     Size in bytes of the segment.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    ///     Whether the segment has been uploaded to cloud storage.
    /// </summary>
    public bool IsCloudDurable { get; init; }

    /// <summary>
    ///     Path to the segment (local or cloud).
    /// </summary>
    public string Path { get; init; } = default!;

    /// <summary>
    ///     When the segment was created (UTC ticks).
    /// </summary>
    public long CreatedAtTicks { get; init; } = DateTime.UtcNow.Ticks;
}
