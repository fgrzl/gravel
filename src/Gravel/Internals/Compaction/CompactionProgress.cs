namespace Gravel.Internals.Compaction;

/// <summary>
///     Simple progress record for compaction tasks.
/// </summary>
public sealed record CompactionProgress(string TaskId, long BytesWritten, long TotalExpected);
