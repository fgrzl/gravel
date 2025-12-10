using Gravel.Actor;
using Gravel.Actor.Messages;
using Microsoft.Extensions.Logging;

namespace Gravel.Actor.Tasks;

/// <summary>
///     Task for evicting an SST file from the local cache.
/// </summary>
public sealed class EvictSstTask : IActorTask
{
    readonly Func<ValueTask> _evictFunc;
    readonly ILogger? _logger;
    readonly string _taskName;

    /// <summary>
    ///     Initializes a new instance of <see cref="EvictSstTask" />.
    /// </summary>
    /// <param name="sourceMessage">The eviction message that triggered this task.</param>
    /// <param name="evictFunc">Delegate to execute the eviction.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public EvictSstTask(
        EvictSstMessage sourceMessage,
        Func<ValueTask> evictFunc,
        ILogger? logger = null)
    {
        SourceMessage = sourceMessage;
        _evictFunc = evictFunc;
        _logger = logger;
        _taskName = $"EvictSST({sourceMessage.SstPath})";
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
    ///     Gets the estimated duration.
    /// </summary>
    public long? EstimatedDurationMs => null;

    /// <summary>
    ///     Executes the SST eviction operation.
    /// </summary>
    public async ValueTask ExecuteAsync(CancellationToken ct = default)
    {
        _logger?.LogDebug("SST eviction task executing");
        await _evictFunc().ConfigureAwait(false);
        _logger?.LogInformation("SST eviction task completed");
    }

    /// <summary>
    ///     Disposes the task.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
