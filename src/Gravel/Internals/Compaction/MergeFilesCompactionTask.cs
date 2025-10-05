using System.Diagnostics;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Engine;
using Gravel.Logging;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Internals.Compaction;

/// <summary>
///     Compaction task that merges a set of SST files into a single output SST.
///     Runs the merge/writer in the background and invokes a completion callback
///     to let the DbEngine update levels and remove old files.
///     This implementation performs the full compaction work inside the task and
///     cooperates with the CompactionWorker by being enqueued and awaited via
///     the worker's lifecycle (PrepareAsync/CompleteAsync).
/// </summary>
public sealed class MergeFilesCompactionTask : ICompactionTask
{
    readonly int _expectedEntries;
    readonly List<SstFile> _inputs;
    readonly ILogger _logger;
    readonly Action<string, List<SstFile>> _onSuccess;
    readonly string _outPath;
    readonly ISstFactory _sstFactory;

    Task? _work;

    /// <summary>
    ///     Initializes a new instance of <see cref="MergeFilesCompactionTask" />.
    /// </summary>
    /// <param name="inputs">The input SST files to merge.</param>
    /// <param name="outPath">The output SST file path.</param>
    /// <param name="sstFactory">The SST factory for creating readers/writers.</param>
    /// <param name="expectedEntries">The expected number of entries in the output.</param>
    /// <param name="onSuccess">Callback invoked on successful completion.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public MergeFilesCompactionTask(
        List<SstFile> inputs, string outPath, ISstFactory sstFactory,
        int expectedEntries, Action<string, List<SstFile>> onSuccess, ILogger? logger = null)
    {
        _inputs = inputs;
        _outPath = outPath;
        _sstFactory = sstFactory;
        _expectedEntries = expectedEntries;
        _onSuccess = onSuccess;
        _logger = logger ?? NullLogger.Instance;
        TaskId = Path.GetFileName(outPath);

        // estimate total bytes expected as sum of input file sizes when available
        long total = 0;
        foreach (var f in inputs)
            try
            {
                if (File.Exists(f.Path)) total += new FileInfo(f.Path).Length;
            }
            catch
            {
            }

        TotalBytesExpected = total;
    }

    /// <summary>
    ///     Gets the unique task ID for this compaction task.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    ///     Gets the total bytes expected to be written by this compaction task.
    /// </summary>
    public long TotalBytesExpected { get; }

    /// <summary>
    ///     Prepares resources and starts the compaction work in the background.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the preparation.</returns>
    public Task PrepareAsync(CancellationToken ct)
    {
        // Start compaction work in background; we will await it in CompleteAsync.
        _work = Task.Run(async () =>
        {
            try
            {
                using var act = TelemetryHelper.StartActivityScope(TelemetrySources.ActivitySource, _logger,
                    "Compaction.Task", ActivityKind.Internal,
                    new KeyValuePair<string, object?>("out.path", _outPath));

                Log.CompactionStarted(_logger, -1, _inputs.Count, -1);

                // Use async factory to create writer and ensure it's initialized before writing
                await using (var w = await _sstFactory.CreateWriterAsync(_outPath, _expectedEntries, ct)
                                 .ConfigureAwait(false))
                {
                    await w.WriteAsync(Compactor.MergeLevelFilesAsync(_inputs, CancellationToken.None))
                        .ConfigureAwait(false);
                }

                // on success, invoke engine callback to install SST and remove inputs
                try
                {
                    _onSuccess(_outPath, _inputs);
                    TelemetrySources.Compactions.Add(1);
                    Log.CompactionFinished(_logger, _outPath);
                }
                catch (Exception ex)
                {
                    Log.SstDeleteFailed(_logger, _outPath, ex.Message);
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log.CompactionError(_logger, ex.Message);
                throw;
            }
        }, ct);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Reads up to buffer.Length bytes from the task's input(s) into buffer.
    ///     This implementation does not stream input; returns 0.
    /// </summary>
    /// <param name="buffer">The buffer to read into.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task with the number of bytes read (always 0).</returns>
    public Task<int> ReadNextInputAsync(Memory<byte> buffer, CancellationToken ct)
    {
        // No streaming-bytes integration: compaction runs in its own background task
        // and the worker will simply wait for completion via CompleteAsync.
        return Task.FromResult(0);
    }

    /// <summary>
    ///     Writes an output chunk. This implementation is a no-op; actual writes are performed by the SST writer.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task WriteOutputAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        // No-op for this simple task variant: actual writes are performed by the sst writer
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Awaits completion of the background compaction work and releases resources.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing completion.</returns>
    public async Task CompleteAsync(CancellationToken ct)
    {
        if (_work != null) await _work.ConfigureAwait(false);
    }
}
