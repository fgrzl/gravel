using System.Buffers;
using System.Threading.Channels;

namespace Gravel.Internals.Compaction;

/// <summary>
///     Background compaction worker. Queues tasks, enforces concurrency and throttling,
///     and reuses buffers via ArrayPool.
/// </summary>
public sealed class CompactionWorker : ICompactionWorker
{
    readonly int _bufferSize;
    readonly SemaphoreSlim _concurrency;
    readonly CancellationTokenSource _cts = new();
    readonly TokenBucketLimiter _limiter;
    readonly Channel<ICompactionTask> _queue;

    public CompactionWorker(double bytesPerSecond, int maxConcurrent = 1, int bufferSize = 64 * 1024)
    {
        _queue = Channel.CreateUnbounded<ICompactionTask>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });
        _limiter = new TokenBucketLimiter(bytesPerSecond, Math.Max(bytesPerSecond, bufferSize));
        _concurrency = new SemaphoreSlim(maxConcurrent);
        _bufferSize = bufferSize;
        _ = Task.Run(() => DispatchLoopAsync(_cts.Token));
    }

    public event Action<CompactionProgress>? ProgressChanged;

    public ValueTask EnqueueAsync(ICompactionTask task, CancellationToken ct = default)
    {
        return _queue.Writer.WriteAsync(task, ct);
    }

    public void UpdateBytesPerSecond(double bytesPerSecond)
    {
        _limiter.UpdateRate(bytesPerSecond, Math.Max(bytesPerSecond, _bufferSize));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _concurrency.Dispose();
    }

    async Task DispatchLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var task in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await _concurrency.WaitAsync(ct).ConfigureAwait(false);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessTaskAsync(task, ct).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // swallow - task-specific errors should be handled/logged by host
                    }
                    finally
                    {
                        _concurrency.Release();
                    }
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    async Task ProcessTaskAsync(ICompactionTask task, CancellationToken ct)
    {
        await task.PrepareAsync(ct).ConfigureAwait(false);
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(_bufferSize);
        try
        {
            long totalWritten = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var mem = new Memory<byte>(buffer, 0, _bufferSize);
                var read = await task.ReadNextInputAsync(mem, ct).ConfigureAwait(false);
                if (read == 0) break;

                // Wait until limiter allows these bytes to be consumed.
                await _limiter.WaitToConsumeAsync(read, ct).ConfigureAwait(false);

                var offset = 0;
                const int chunkMax = 16 * 1024;
                while (offset < read)
                {
                    var chunk = Math.Min(chunkMax, read - offset);
                    await task.WriteOutputAsync(mem.Slice(offset, chunk), ct).ConfigureAwait(false);
                    offset += chunk;
                    totalWritten += chunk;
                    ProgressChanged?.Invoke(new CompactionProgress(task.TaskId, totalWritten, task.TotalBytesExpected));
                    await Task.Yield();
                }
            }

            await task.CompleteAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            pool.Return(buffer);
        }
    }
}
