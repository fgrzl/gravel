using Gravel.Actor;
using Gravel.Actor.Messages;
using Microsoft.Extensions.Logging;

namespace Gravel.Actor.Tasks;

/// <summary>
///     Task for executing a single compaction pass (merging files from one level to the next).
/// </summary>
public sealed class CompactionTask : IActorTask
{
    readonly Func<ValueTask> _compactFunc;
    readonly ILogger? _logger;
    readonly string _taskName;
    int _retryCount;

    /// <summary>
    ///     Initializes a new instance of <see cref="CompactionTask" />.
    /// </summary>
    /// <param name="sourceMessage">The compaction schedule message that triggered this task.</param>
    /// <param name="levelFrom">The source level (0-based).</param>
    /// <param name="levelTo">The destination level.</param>
    /// <param name="fileCount">Number of files being compacted.</param>
    /// <param name="compactFunc">Delegate to execute the compaction.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public CompactionTask(
        ScheduleCompactionMessage sourceMessage,
        int levelFrom,
        int levelTo,
        int fileCount,
        Func<ValueTask> compactFunc,
        ILogger? logger = null)
    {
        SourceMessage = sourceMessage;
        _compactFunc = compactFunc;
        _logger = logger;
        _taskName = $"Compact(L{levelFrom}->L{levelTo}, files={fileCount})";
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
    public long? EstimatedDurationMs => null; // Depends on file sizes

    /// <summary>
    ///     Executes the compaction operation.
    /// </summary>
    public async ValueTask ExecuteAsync(CancellationToken ct = default)
    {
        _logger?.LogDebug("Compaction task executing");
        await _compactFunc().ConfigureAwait(false);
        _logger?.LogInformation("Compaction task completed");
    }

    /// <summary>
    ///     Handles compaction failures with retry logic.
    /// </summary>
    public async ValueTask<bool> HandleFailureAsync(Exception exception, CancellationToken ct = default)
    {
        _logger?.LogWarning(exception, "Compaction task failed (attempt {Attempt})", _retryCount + 1);
        _retryCount++;
        // Retry up to 3 times for transient failures
        return _retryCount < 3;
    }

    /// <summary>
    ///     Disposes the task.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
