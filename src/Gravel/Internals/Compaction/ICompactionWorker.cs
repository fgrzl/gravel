namespace Gravel.Internals.Compaction;

/// <summary>
///     Abstraction over a compaction worker so different implementations can be swapped in.
/// </summary>
public interface ICompactionWorker : IDisposable
{
    /// <summary>
    ///     Enqueue a compaction task for background processing.
    /// </summary>
    ValueTask EnqueueAsync(ICompactionTask task, CancellationToken ct = default);

    /// <summary>
    ///     Update the global bytes-per-second rate used by the worker.
    /// </summary>
    void UpdateBytesPerSecond(double bytesPerSecond);

    /// <summary>
    ///     Progress change notifications from the active compaction task(s).
    /// </summary>
    event Action<CompactionProgress>? ProgressChanged;
}