namespace Gravel.Internals.Compaction;

/// <summary>
///     Very small adaptive controller that can be used to adjust compaction rate based on memory pressure.
///     Hosts can implement more sophisticated logic and feed signals directly into CompactionWorker.UpdateBytesPerSecond.
/// </summary>
public sealed class AdaptiveController : IDisposable
{
    readonly CancellationTokenSource _cts = new();
    readonly TimeSpan _interval;
    readonly double _maxRate;
    readonly double _minRate;
    readonly CompactionWorker _worker;

    public AdaptiveController(CompactionWorker worker, double minRate, double maxRate, TimeSpan? interval = null)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _minRate = Math.Max(1, minRate);
        _maxRate = Math.Max(_minRate, maxRate);
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _ = Task.Run(RunAsync);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // simple heuristic: reduce rate when GC memory usage is high.
                var used = GC.GetTotalMemory(false);
                // thresholds are heuristic - hosts should tune these values.
                var ratio = Math.Clamp(used / (1024.0 * 1024.0) / 1024.0, 0.0, 1.0); // used GB / 1GB
                // invert ratio so high memory -> low rate
                var newRate = _maxRate - (_maxRate - _minRate) * ratio;
                _worker.UpdateBytesPerSecond(newRate);
                await Task.Delay(_interval, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
