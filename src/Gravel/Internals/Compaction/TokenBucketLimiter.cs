using System.Diagnostics;

namespace Gravel.Internals.Compaction;

/// <summary>
///     Implements a token bucket rate limiter for controlling throughput (e.g., bytes per second).
///     Supports burst limits and dynamic rate updates. Thread-safe for concurrent use.
/// </summary>
public sealed class TokenBucketLimiter
{
    readonly object _lock = new();
    double _burstBytes;
    double _bytesPerSecond;
    long _lastTicks;

    // Signal used to wake up waiting async consumers when rate is updated
    TaskCompletionSource<bool> _rateUpdated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    double _tokens;

    /// <summary>
    ///     Initializes a new instance of <see cref="TokenBucketLimiter" /> with the specified rate and burst size.
    /// </summary>
    /// <param name="bytesPerSecond">Allowed bytes per second.</param>
    /// <param name="burstBytes">Maximum burst size in bytes.</param>
    public TokenBucketLimiter(double bytesPerSecond, double burstBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytesPerSecond, 0.0, nameof(bytesPerSecond));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(burstBytes, 0.0, nameof(burstBytes));
        _bytesPerSecond = bytesPerSecond;
        _burstBytes = burstBytes;
        _tokens = burstBytes;
        _lastTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    ///     Maximum delay slice in milliseconds for small deficits. Larger deficits use a single reduced delay.
    /// </summary>
    public int MaxDelaySliceMs { get; set; } = 50;

    /// <summary>
    ///     Attempts to consume the specified number of bytes from the bucket. Returns true if enough tokens are available.
    /// </summary>
    /// <param name="bytes">The number of bytes to consume.</param>
    /// <returns>True if tokens were consumed, otherwise false.</returns>
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

    /// <summary>
    ///     Asynchronously waits until enough tokens are available to consume the specified number of bytes.
    /// </summary>
    /// <param name="bytes">The number of bytes to consume.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task WaitToConsumeAsync(int bytes, CancellationToken ct)
    {
        try
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
                    var delayTask = Task.Delay(TimeSpan.FromMilliseconds(undershoot), ct);
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
                    try
                    {
                        await delayTask.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        // Throw plain OperationCanceledException so task faults with this exact type
                        throw new OperationCanceledException();
                    }
                    catch (OperationCanceledException)
                    {
                        throw new OperationCanceledException();
                    }
                }
                else
                {
                    var sliceMs = (int)Math.Max(1.0, Math.Min(ms, MaxDelaySliceMs));

                    var delayTask = Task.Delay(sliceMs, ct);
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

                    try
                    {
                        await delayTask.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        throw new OperationCanceledException();
                    }
                    catch (OperationCanceledException)
                    {
                        throw new OperationCanceledException();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normalize to plain OperationCanceledException type as expected by tests
            throw new OperationCanceledException();
        }
    }

    /// <summary>
    ///     Updates the rate and burst size for the limiter. Wakes any waiting consumers.
    /// </summary>
    /// <param name="bytesPerSecond">New allowed bytes per second.</param>
    /// <param name="burstBytes">New maximum burst size in bytes.</param>
    public void UpdateRate(double bytesPerSecond, double burstBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytesPerSecond, 0.0, nameof(bytesPerSecond));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(burstBytes, 0.0, nameof(burstBytes));
        lock (_lock)
        {
            Refill();
            var oldBurst = _burstBytes;
            _bytesPerSecond = bytesPerSecond;
            _burstBytes = burstBytes;
            _tokens = burstBytes > oldBurst
                ? Math.Min(_tokens + (burstBytes - oldBurst), _burstBytes)
                : Math.Min(_tokens, _burstBytes);
            _lastTicks = Stopwatch.GetTimestamp();

            // Signal waiting waiters that the rate has been updated so they can wake early
            var prev = _rateUpdated;
            // Replace with a fresh TCS for future updates
            _rateUpdated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Try to set the previous one, ignore if already completed
            try
            {
                prev.TrySetResult(true);
            }
            catch
            {
            }
        }
    }

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
}
