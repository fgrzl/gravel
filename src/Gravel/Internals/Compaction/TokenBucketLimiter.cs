using System.Diagnostics;

namespace Gravel.Internals.Compaction;

public sealed class TokenBucketLimiter
{
    readonly object _lock = new();
    double _burstBytes;
    double _bytesPerSecond;
    long _lastTicks;
    double _tokens;

    // Signal used to wake up waiting async consumers when rate is updated
    TaskCompletionSource<bool> _rateUpdated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TokenBucketLimiter(double bytesPerSecond, double burstBytes)
    {
        if (bytesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        if (burstBytes <= 0) throw new ArgumentOutOfRangeException(nameof(burstBytes));
        _bytesPerSecond = bytesPerSecond;
        _burstBytes = burstBytes;
        _tokens = burstBytes;
        _lastTicks = Stopwatch.GetTimestamp();
    }

    // Max delay slice in milliseconds for small deficits. Larger deficits use a single reduced delay.
    public int MaxDelaySliceMs { get; set; } = 50;

    void Refill()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastTicks) / (double)Stopwatch.Frequency;
        if (elapsed > 0)
        {
            _tokens = Math.Min(_burstBytes, _tokens + elapsed * _bytesPerSecond);
            _lastTicks = now;
        }
    }

    public bool TryConsume(int bytes)
    {
        if (bytes <= 0) return true;
        lock (_lock)
        {
            Refill();
            if (_tokens >= bytes)
            {
                _tokens -= bytes;
                return true;
            }

            return false;
        }
    }

    public async Task WaitToConsumeAsync(int bytes, CancellationToken ct)
    {
        if (bytes <= 0) return;

        var remaining = (double)bytes;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();

            double need;
            lock (_lock)
            {
                Refill();
                var can = Math.Min(_tokens, remaining);
                if (can >= 1.0 || remaining < 1.0)
                {
                    _tokens -= can;
                    remaining -= can;
                    continue;
                }

                var target = Math.Min(remaining, _burstBytes);
                need = Math.Max(0.0, target - _tokens);
            }

            if (remaining <= 0) break;

            var ms = need / _bytesPerSecond * 1000.0;
            if (ms > MaxDelaySliceMs * 2)
            {
                // Undershoot slightly to avoid oversleep due to coarse timers; the remainder will be made up next loop.
                var undershoot = Math.Max(1.0, ms * 0.9);

                // Create delay task and race it against a rate-update signal so UpdateRate can wake us early.
                Task delayTask = Task.Delay(TimeSpan.FromMilliseconds(undershoot), ct);
                Task rateTask;
                lock (_lock)
                {
                    rateTask = _rateUpdated.Task;
                }

                var winner = await Task.WhenAny(delayTask, rateTask).ConfigureAwait(false);
                if (winner == rateTask)
                {
                    // rate was updated; continue to refill and try again without waiting the full delay
                    continue;
                }

                // otherwise, await delayTask to observe cancellation or exceptions
                await delayTask.ConfigureAwait(false);
            }
            else
            {
                var sliceMs = (int)Math.Max(1.0, Math.Min(ms, MaxDelaySliceMs));

                Task delayTask = Task.Delay(sliceMs, ct);
                Task rateTask;
                lock (_lock)
                {
                    rateTask = _rateUpdated.Task;
                }

                var winner = await Task.WhenAny(delayTask, rateTask).ConfigureAwait(false);
                if (winner == rateTask)
                {
                    continue;
                }

                await delayTask.ConfigureAwait(false);
            }
        }
    }

    public void UpdateRate(double bytesPerSecond, double burstBytes)
    {
        if (bytesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        if (burstBytes <= 0) throw new ArgumentOutOfRangeException(nameof(burstBytes));
        lock (_lock)
        {
            Refill();
            var oldBurst = _burstBytes;
            _bytesPerSecond = bytesPerSecond;
            _burstBytes = burstBytes;
            _tokens = burstBytes > oldBurst ? Math.Min(_tokens + (burstBytes - oldBurst), _burstBytes) : Math.Min(_tokens, _burstBytes);
            _lastTicks = Stopwatch.GetTimestamp();

            // Signal waiting waiters that the rate has been updated so they can wake early
            var prev = _rateUpdated;
            // Replace with a fresh TCS for future updates
            _rateUpdated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Try to set the previous one, ignore if already completed
            try { prev.TrySetResult(true); } catch { }
        }
    }
}
