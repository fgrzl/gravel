namespace Gravel.Engine.Compaction;

/// <summary>
///     Abstraction for a single streaming compaction task. Implementations should perform
///     streaming reads from input SSTables and streaming writes to output SSTables.
///     All methods should be cooperative with cancellation tokens.
/// </summary>
public interface ICompactionTask
{
    string TaskId { get; }
    long TotalBytesExpected { get; }

    /// <summary>
    ///     Prepare resources (open files/iterators).
    /// </summary>
    Task PrepareAsync(CancellationToken ct);

    /// <summary>
    ///     Read up to buffer.Length bytes from the task's input(s) into buffer.
    ///     Return number of bytes read. Return 0 when no more input remains.
    /// </summary>
    Task<int> ReadNextInputAsync(Memory<byte> buffer, CancellationToken ct);

    /// <summary>
    ///     Write an output chunk. Implementations should stream to disk and not buffer
    ///     all data in memory.
    /// </summary>
    Task WriteOutputAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct);

    /// <summary>
    ///     Complete and release resources.
    /// </summary>
    Task CompleteAsync(CancellationToken ct);
}