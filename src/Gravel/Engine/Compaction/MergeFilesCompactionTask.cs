using System.Diagnostics;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Logging;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Engine.Compaction;

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

    public MergeFilesCompactionTask(List<SstFile> inputs, string outPath, ISstFactory sstFactory,
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

    public string TaskId { get; }

    public long TotalBytesExpected { get; }

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

                await using (var w = _sstFactory.CreateWriter(_outPath, _expectedEntries))
                {
                    await w.WriteAsync(Compactor.MergeLevelFilesAsync(_inputs, CancellationToken.None));
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

    public Task<int> ReadNextInputAsync(Memory<byte> buffer, CancellationToken ct)
    {
        // No streaming-bytes integration: compaction runs in its own background task
        // and the worker will simply wait for completion via CompleteAsync.
        return Task.FromResult(0);
    }

    public Task WriteOutputAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        // No-op for this simple task variant: actual writes are performed by the sst writer
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        if (_work != null) await _work.ConfigureAwait(false);
    }
}