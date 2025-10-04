using System.Diagnostics;

namespace Gravel.Internals.Compaction;

public sealed class TokenBucketLimiter
{
    readonly object _lock = new();
    double _burstBytes;
    double _bytesPerSecond;
    long _lastTicks;
    double _tokens;

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
            _tokens = Math.Min(_burstBytes, _tokens + (elapsed * _bytesPerSecond));
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
                await Task.Delay(TimeSpan.FromMilliseconds(undershoot), ct).ConfigureAwait(false);
            }
            else
            {
                var sliceMs = (int)Math.Max(1.0, Math.Min(ms, MaxDelaySliceMs));
                await Task.Delay(sliceMs, ct).ConfigureAwait(false);
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
            if (burstBytes > oldBurst)
                _tokens = Math.Min(_tokens + (burstBytes - oldBurst), _burstBytes);
            else
                _tokens = Math.Min(_tokens, _burstBytes);
            _lastTicks = Stopwatch.GetTimestamp();
        }
    }
}