namespace Gravel.Internals.Compaction;

/// <summary>
///     Adaptive controller for compaction rate. Adjusts compaction throughput based on memory pressure.
///     Hosts can implement more sophisticated logic and feed signals directly into CompactionWorker.UpdateBytesPerSecond.
/// </summary>
public sealed class AdaptiveController : IDisposable
{
    readonly CancellationTokenSource _cts = new();
    readonly TimeSpan _interval;
    readonly double _maxRate;
    readonly double _minRate;
    readonly CompactionWorker _worker;

    /// <summary>
    ///     Initializes a new instance of <see cref="AdaptiveController"/>.
    /// </summary>
    /// <param name="worker">The compaction worker to control.</param>
    /// <param name="minRate">Minimum bytes per second.</param>
    /// <param name="maxRate">Maximum bytes per second.</param>
    /// <param name="interval">Optional interval for rate adjustment.</param>
    public AdaptiveController(CompactionWorker worker, double minRate, double maxRate, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(worker, nameof(worker));
        _worker = worker;
        _minRate = Math.Max(1, minRate);
        _maxRate = Math.Max(_minRate, maxRate);
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _ = Task.Run(RunAsync);
    }

    /// <summary>
    ///     Disposes the controller and cancels background rate adjustment.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <summary>
    ///     Background loop that periodically adjusts compaction rate based on GC memory usage.
    /// </summary>
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
