using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Gravel.Internals.Compaction;

public class CompactionWorkerTests
{
    [Fact]
    public async Task should_process_task_and_write_output_given_simple_inputs_when_enqueued()
    {
        // Arrange
        var inputs = new[]
        {
            "hello "u8.ToArray(),
            "world"u8.ToArray(),
            "!"u8.ToArray()
        };

        var task = new FakeCompactionTask("t1", inputs);
        using var worker = new CompactionWorker(10_000_000);

        // Act
        await worker.EnqueueAsync(task);
        var completed = await task.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.True(completed);
        var output = task.GetOutputBytes();
        Assert.Equal("hello world!", Encoding.UTF8.GetString(output));
    }

    [Fact]
    public async Task should_report_progress_given_task_when_processing()
    {
        // Arrange
        var inputs = new[] { "abc"u8.ToArray(), "def"u8.ToArray() };
        var task = new FakeCompactionTask("t2", inputs);
        using var worker = new CompactionWorker(10_000_000);

        var reports = new List<CompactionProgress>();
        worker.ProgressChanged += p => reports.Add(p);

        // Act
        await worker.EnqueueAsync(task);
        var completed = await task.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        Assert.True(completed);
        Assert.NotEmpty(reports);
        var final = reports[^1];
        Assert.Equal("t2", final.TaskId);
        Assert.Equal(task.TotalBytesExpected, final.BytesWritten);
    }

    [Fact]
    public void should_update_rate_given_small_burst_when_try_consume_and_update()
    {
        // Arrange
        var limiter = new TokenBucketLimiter(10, 10);

        // Act & Assert
        Assert.True(limiter.TryConsume(10)); // burst allows immediate 10 bytes
        Assert.False(limiter.TryConsume(1)); // no tokens remain

        // Act
        limiter.UpdateRate(1000, 1000);

        // Assert
        Assert.True(limiter.TryConsume(1));
    }
}
