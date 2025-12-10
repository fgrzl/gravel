namespace Gravel.Cloud.Abstractions;

/// <summary>
///     Manifest describing the current state of the database: SST levels, file metadata, sequences, and compaction history.
///     Supports deterministic recovery and compaction replay.
/// </summary>
public interface IManifestManager : IAsyncDisposable
{
    /// <summary>
    ///     Loads the manifest from persistent storage (local or cloud).
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The loaded manifest metadata.</returns>
    ValueTask<ManifestData?> LoadAsync(CancellationToken ct = default);

    /// <summary>
    ///     Saves the current manifest to persistent storage.
    /// </summary>
    /// <param name="manifest">The manifest to save.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask SaveAsync(ManifestData manifest, CancellationToken ct = default);

    /// <summary>
    ///     Records a compaction action in the manifest log for deterministic replay.
    /// </summary>
    /// <param name="action">The compaction action to log.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask LogCompactionActionAsync(CompactionAction action, CancellationToken ct = default);

    /// <summary>
    ///     Retrieves all logged compaction actions in order (for replay).
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of compaction actions in chronological order.</returns>
    ValueTask<IList<CompactionAction>> GetCompactionHistoryAsync(CancellationToken ct = default);
}

/// <summary>
///     Manifest metadata describing the database state.
/// </summary>
public sealed class ManifestData
{
    /// <summary>
    ///     Version/timestamp of this manifest.
    /// </summary>
    public string Version { get; init; } = default!;

    /// <summary>
    ///     The last known sequence number.
    /// </summary>
    public ulong LastSequence { get; init; }

    /// <summary>
    ///     Metadata about all SST files in each level.
    /// </summary>
    public Dictionary<int, List<SstFileInfo>> LevelFiles { get; init; } = [];

    /// <summary>
    ///     Metadata about all WAL segments.
    /// </summary>
    public List<WalSegmentInfo> WalSegments { get; init; } = [];

    /// <summary>
    ///     Opaque metadata for recovery and compaction decisions.
    /// </summary>
    public Dictionary<string, object?> Metadata { get; init; } = [];

    /// <summary>
    ///     When this manifest was created.
    /// </summary>
    public long CreatedAtTicks { get; init; } = DateTime.UtcNow.Ticks;
}

/// <summary>
///     Metadata about a single SST file.
/// </summary>
public sealed class SstFileInfo
{
    /// <summary>
    ///     Cloud path to the SST file.
    /// </summary>
    public string Path { get; init; } = default!;

    /// <summary>
    ///     Size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    ///     Smallest key in the file (for range queries).
    /// </summary>
    public ReadOnlyMemory<byte> SmallestKey { get; init; }

    /// <summary>
    ///     Largest key in the file (for range queries).
    /// </summary>
    public ReadOnlyMemory<byte> LargestKey { get; init; }

    /// <summary>
    ///     Sequence number range [min, max] for entries in this file.
    /// </summary>
    public (ulong Min, ulong Max) SequenceRange { get; init; }

    /// <summary>
    ///     When the file was created.
    /// </summary>
    public long CreatedAtTicks { get; init; } = DateTime.UtcNow.Ticks;
}

/// <summary>
///     A compaction action recorded in the manifest for deterministic replay.
/// </summary>
public sealed class CompactionAction
{
    /// <summary>
    ///     Unique identifier for this compaction.
    /// </summary>
    public Guid CompactionId { get; init; } = Guid.NewGuid();

    /// <summary>
    ///     The level being compacted from (0-based).
    /// </summary>
    public int FromLevel { get; init; }

    /// <summary>
    ///     The level being compacted to.
    /// </summary>
    public int ToLevel { get; init; }

    /// <summary>
    ///     Paths of input SST files.
    /// </summary>
    public List<string> InputPaths { get; init; } = [];

    /// <summary>
    ///     Path of the output SST file.
    /// </summary>
    public string OutputPath { get; init; } = default!;

    /// <summary>
    ///     When this compaction was initiated (UTC ticks).
    /// </summary>
    public long CreatedAtTicks { get; init; } = DateTime.UtcNow.Ticks;

    /// <summary>
    ///     Status of the compaction action.
    /// </summary>
    public CompactionActionStatus Status { get; set; } = CompactionActionStatus.Pending;
}

/// <summary>
///     Status of a compaction action.
/// </summary>
public enum CompactionActionStatus
{
    /// <summary>
    ///     Compaction has been planned but not yet started.
    /// </summary>
    Pending,

    /// <summary>
    ///     Compaction is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    ///     Compaction completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    ///     Compaction failed.
    /// </summary>
    Failed
}
