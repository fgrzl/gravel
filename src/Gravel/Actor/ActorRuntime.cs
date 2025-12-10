using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Gravel.Actor;

/// <summary>
///     Default implementation of the actor runtime.
///     Maintains strict FIFO ordering of messages and tasks for determinism.
/// </summary>
public sealed class ActorRuntime : IActorRuntime
{
    readonly Channel<ActorMessage> _messageQueue =
        Channel.CreateUnbounded<ActorMessage>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

    readonly Channel<IActorTask> _taskQueue =
        Channel.CreateUnbounded<IActorTask>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

    readonly CancellationTokenSource _cts = new();
    readonly ILogger<ActorRuntime> _logger;
    readonly List<ActorIntentLogEntry> _intentLog = [];
    readonly object _statsLock = new();

    long _messagesProcessed;
    long _tasksCompleted;
    long _taskFailures;
    long _totalExecutionMs;

    /// <summary>
    ///     Initializes a new instance of <see cref="ActorRuntime" />.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public ActorRuntime(ILogger<ActorRuntime> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Actor runtime initialized");
        
        // Start background dispatcher loop
        _ = DispatcherLoopAsync(_cts.Token);
    }

    /// <summary>
    ///     Posts a message to the actor queue for processing.
    /// </summary>
    public ValueTask PostMessageAsync(ActorMessage message, CancellationToken ct = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        return _messageQueue.Writer.WriteAsync(message, ct);
    }

    /// <summary>
    ///     Enqueues a task for execution.
    /// </summary>
    public ValueTask EnqueueTaskAsync(IActorTask task, CancellationToken ct = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        return _taskQueue.Writer.WriteAsync(task, ct);
    }

    /// <summary>
    ///     Returns the current intent log (read-only snapshot).
    /// </summary>
    public IReadOnlyList<ActorIntentLogEntry> GetIntentLog()
    {
        lock (_statsLock)
        {
            return _intentLog.AsReadOnly();
        }
    }

    /// <summary>
    ///     Waits for all enqueued tasks to complete.
    /// </summary>
    public async ValueTask WaitForQuiesceAsync(CancellationToken ct = default)
    {
        // Wait until message queue is empty and all tasks are processed
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            lock (_statsLock)
            {
                if (_messageQueue.Reader.Count == 0 && _taskQueue.Reader.Count == 0)
                    break;
            }
            await Task.Delay(10, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Returns statistics about actor runtime performance.
    /// </summary>
    public ActorRuntimeStats GetStats()
    {
        lock (_statsLock)
        {
            return new ActorRuntimeStats
            {
                MessagesProcessed = _messagesProcessed,
                TasksCompleted = _tasksCompleted,
                TaskFailures = _taskFailures,
                PendingTasks = _taskQueue.Reader.Count,
                TotalExecutionMs = _totalExecutionMs
            };
        }
    }

    /// <summary>
    ///     Disposes the actor runtime and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Task.Delay(100); // Allow dispatcher to wind down gracefully
        _cts.Dispose();
        _messageQueue.Writer.TryComplete();
        _taskQueue.Writer.TryComplete();
        _logger.LogInformation("Actor runtime disposed");
    }

    async Task DispatcherLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Interleave message and task processing to avoid starvation
                var msgTask = _messageQueue.Reader.WaitToReadAsync(ct);
                var taskTask = _taskQueue.Reader.WaitToReadAsync(ct);

                await Task.WhenAny(
                    msgTask.AsTask(),
                    taskTask.AsTask()
                ).ConfigureAwait(false);

                // Process one message if available
                if (await msgTask.ConfigureAwait(false))
                {
                    if (_messageQueue.Reader.TryRead(out var message))
                    {
                        await ProcessMessageAsync(message, ct).ConfigureAwait(false);
                    }
                }

                // Process one task if available
                if (await taskTask.ConfigureAwait(false))
                {
                    if (_taskQueue.Reader.TryRead(out var task))
                    {
                        await ProcessTaskAsync(task, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Actor dispatcher loop failed");
        }
    }

    async Task ProcessMessageAsync(ActorMessage message, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Processing message: {Source} (id={MessageId})", message.Source, message.MessageId);

            lock (_statsLock)
            {
                Interlocked.Increment(ref _messagesProcessed);
            }

            // Messages are processed by derived implementations; this base class just logs.
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
        }
    }

    async Task ProcessTaskAsync(IActorTask task, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Executing task: {TaskName} (id={TaskId})", task.TaskName, task.TaskId);

            await task.ExecuteAsync(ct).ConfigureAwait(false);

            sw.Stop();
            _logger.LogDebug("Task completed: {TaskName} (duration={DurationMs}ms)",
                task.TaskName, sw.ElapsedMilliseconds);

            lock (_statsLock)
            {
                Interlocked.Increment(ref _tasksCompleted);
                _totalExecutionMs += sw.ElapsedMilliseconds;

                // Record in intent log
                if (task.SourceMessage is ActorMessage sourceMsg)
                {
                    var entry = new ActorIntentLogEntry
                    {
                        SourceMessage = sourceMsg,
                        ScheduledTask = task,
                        Status = ActorIntentStatus.Completed,
                        Metadata = new Dictionary<string, object?>
                        {
                            { "duration_ms", sw.ElapsedMilliseconds }
                        }
                    };
                    _intentLog.Add(entry);
                }
            }

            await task.OnCompleteAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Task failed: {TaskName} (id={TaskId}, duration={DurationMs}ms)",
                task.TaskName, task.TaskId, sw.ElapsedMilliseconds);

            lock (_statsLock)
            {
                Interlocked.Increment(ref _taskFailures);

                // Record failure in intent log
                if (task.SourceMessage is ActorMessage sourceMsg)
                {
                    var entry = new ActorIntentLogEntry
                    {
                        SourceMessage = sourceMsg,
                        ScheduledTask = task,
                        Status = ActorIntentStatus.Failed,
                        ErrorMessage = ex.Message,
                        Metadata = new Dictionary<string, object?>
                        {
                            { "duration_ms", sw.ElapsedMilliseconds }
                        }
                    };
                    _intentLog.Add(entry);
                }
            }

            // Allow task to handle the failure (e.g., retry)
            var shouldRetry = await task.HandleFailureAsync(ex, ct).ConfigureAwait(false);
            if (shouldRetry)
            {
                _logger.LogInformation("Retrying task: {TaskName}", task.TaskName);
                // Re-enqueue the task
                try
                {
                    await EnqueueTaskAsync(task, ct).ConfigureAwait(false);
                }
                catch (Exception requeueEx)
                {
                    _logger.LogError(requeueEx, "Failed to re-enqueue task {TaskName}", task.TaskName);
                }
            }
        }
        finally
        {
            try
            {
                await task.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing task {TaskName}", task.TaskName);
            }
        }
    }
}
