using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Gravel.Internals.Compaction;

public class TokenBucketLimiterTests
{
    [Fact]
    public async Task should_return_immediately_given_sufficient_tokens_when_wait_to_consume()
    {
        // Arrange
        var limiter = new TokenBucketLimiter(10_000, 10_000) { MaxDelaySliceMs = 5 };

        // Act
        var sw = Stopwatch.StartNew();
        await limiter.WaitToConsumeAsync(1024, CancellationToken.None);
        sw.Stop();

        // Assert
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task should_limit_without_deadlock_given_request_larger_than_burst_when_wait_to_consume()
    {
        // Arrange: burst covers 100 immediately, the rest 200 at 1000 B/s ≈ 200ms
        var limiter = new TokenBucketLimiter(1000, 100) { MaxDelaySliceMs = 10 };

        // Act
        var sw = Stopwatch.StartNew();
        await limiter.WaitToConsumeAsync(300, CancellationToken.None);
        sw.Stop();

        // Assert
        var expectedMs = (300 - 100) / 1000.0 * 1000.0; // ≈ 200ms
        Assert.True(sw.ElapsedMilliseconds >= (long)(expectedMs * 0.5)); // not immediate
        Assert.True(sw.ElapsedMilliseconds < 2000); // should not take excessively long
    }

    [Fact]
    public async Task should_reduce_wait_time_given_rate_updated_when_waiting_to_consume()
    {
        // Arrange
        var limiter = new TokenBucketLimiter(200, 50) { MaxDelaySliceMs = 10 };
        var bytes = 150; // needs ~0.5s at 200 B/s
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var sw = Stopwatch.StartNew();
        var task = limiter.WaitToConsumeAsync(bytes, cts.Token);
        await Task.Delay(100, cts.Token);
        limiter.UpdateRate(10_000, 10_000);
        await task;
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 400);
    }

    [Fact]
    public async Task should_cancel_promptly_given_cancellation_when_waiting_to_consume()
    {
        // Arrange
        var limiter = new TokenBucketLimiter(50, 1) { MaxDelaySliceMs = 5 };
        var cts = new CancellationTokenSource();
        var t = Task.Run(async () =>
        {
            await Task.Delay(50, cts.Token);
            await cts.CancelAsync();
        });

        // Act
        var sw = Stopwatch.StartNew();
        var act = async () => await limiter.WaitToConsumeAsync(10_000, cts.Token);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000);
        await t;
    }
}
