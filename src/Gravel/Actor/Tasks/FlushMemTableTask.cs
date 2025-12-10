using Gravel.Actor;
using Gravel.Actor.Messages;
using Microsoft.Extensions.Logging;

namespace Gravel.Actor.Tasks;

/// <summary>
///     Task for flushing a memtable to SST and uploading to cloud storage (if enabled).
/// </summary>
public sealed class FlushMemTableTask : IActorTask
{
    readonly Func<ValueTask<string>> _flushMemTableFunc;
    readonly ILogger? _logger;
    readonly string _taskName;

    /// <summary>
    ///     Initializes a new instance of <see cref="FlushMemTableTask" />.
    /// </summary>
    /// <param name="sourceMessage">The flush message that triggered this task.</param>
    /// <param name="flushMemTableFunc">Delegate to execute the flush operation.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public FlushMemTableTask(
        FlushMemTableMessage sourceMessage,
        Func<ValueTask<string>> flushMemTableFunc,
        ILogger? logger = null)
    {
        SourceMessage = sourceMessage;
        _flushMemTableFunc = flushMemTableFunc;
        _logger = logger;
        _taskName = $"FlushMemTable(entries={sourceMessage.EntryCount})";
    }

    /// <summary>
    ///     Gets the unique task identifier.
    /// </summary>
    public Guid TaskId { get; } = Guid.NewGuid();

    /// <summary>
    ///     Gets the source message that triggered this task.
    /// </summary>
    public ActorMessage SourceMessage { get; }

    /// <summary>
    ///     Gets the descriptive name of this task.
    /// </summary>
    public string TaskName => _taskName;

    /// <summary>
    ///     Gets the estimated duration (null = unknown).
    /// </summary>
    public long? EstimatedDurationMs => null; // Depends on memtable size

    /// <summary>
    ///     Executes the flush operation.
    /// </summary>
    public async ValueTask ExecuteAsync(CancellationToken ct = default)
    {
        _logger?.LogDebug("FlushMemTable task executing");
        var sstPath = await _flushMemTableFunc().ConfigureAwait(false);
        _logger?.LogInformation("FlushMemTable completed: sst={SstPath}", sstPath);
    }

    /// <summary>
    ///     Disposes the task.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
