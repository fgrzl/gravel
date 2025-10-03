using System;
using System.Threading;
using System.Threading.Tasks;

namespace Gravel.Engine.Compaction;

// No-op compaction worker used by tests when background compaction is not needed.
sealed class NoopCompactionWorker : ICompactionWorker
{
    public event Action<CompactionProgress>? ProgressChanged;

    public ValueTask EnqueueAsync(ICompactionTask task, CancellationToken ct = default)
    {
        // Do nothing; tests can enqueue tasks but they won't be processed.
        return ValueTask.CompletedTask;
    }

    public void UpdateBytesPerSecond(double bytesPerSecond)
    {
        // No-op
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}