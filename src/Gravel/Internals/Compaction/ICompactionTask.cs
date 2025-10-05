namespace Gravel.Internals.Compaction;

/// <summary>
///     Abstraction for a single streaming compaction task. Implementations should perform
///     streaming reads from input SSTables and streaming writes to output SSTables.
///     All methods should be cooperative with cancellation tokens.
/// </summary>
public interface ICompactionTask
{
    /// <summary>
    ///     Gets the unique task ID for this compaction task.
    /// </summary>
    string TaskId { get; }

    /// <summary>
    ///     Gets the total bytes expected to be processed by this compaction task.
    /// </summary>
    long TotalBytesExpected { get; }

    /// <summary>
    ///     Prepares resources (open files/iterators) for the compaction task.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous preparation.</returns>
    Task PrepareAsync(CancellationToken ct);

    /// <summary>
    ///     Reads up to <c>buffer.Length</c> bytes from the task's input(s) into <c>buffer</c>.
    ///     Returns the number of bytes read. Returns 0 when no more input remains.
    /// </summary>
    /// <param name="buffer">The buffer to read into.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task with the number of bytes read.</returns>
    Task<int> ReadNextInputAsync(Memory<byte> buffer, CancellationToken ct);

    /// <summary>
    ///     Writes an output chunk. Implementations should stream to disk and not buffer all data in memory.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    Task WriteOutputAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct);

    /// <summary>
    ///     Completes the compaction task and releases resources.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous completion.</returns>
    Task CompleteAsync(CancellationToken ct);
}
