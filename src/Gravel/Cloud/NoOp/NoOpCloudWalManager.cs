using Gravel.Abstractions;
using Gravel.Cloud.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Cloud.NoOp;

/// <summary>
///     No-op implementation of cloud WAL manager. Supports local-only deployments.
///     All cloud operations complete successfully but don't persist to external storage.
/// </summary>
public sealed class NoOpCloudWalManager : ICloudWalManager
{
    readonly ILogger _logger;
    ulong _lastDurableSequence;

    /// <summary>
    ///     Initializes a new instance of <see cref="NoOpCloudWalManager" />.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public NoOpCloudWalManager(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    ///     Gets the last durable sequence number.
    /// </summary>
    public ulong LastDurableSequence => _lastDurableSequence;

    /// <summary>
    ///     Appends an entry (no-op in this implementation).
    /// </summary>
    public ValueTask AppendAsync(DbEntry entry, CancellationToken ct = default)
    {
        _lastDurableSequence = entry.Sequence;
        _logger.LogTrace("WAL append: seq={Sequence}", entry.Sequence);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Flushes the WAL segment (no-op in this implementation).
    /// </summary>
    public ValueTask FlushSegmentAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("WAL segment flush (no-op)");
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Recovers from WAL (no entries in no-op implementation).
    /// </summary>
    public ValueTask RecoverAsync(Func<DbEntry, ValueTask> onEntry, CancellationToken ct = default)
    {
        // No entries to recover in no-op implementation
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Gets WAL segments (empty in no-op implementation).
    /// </summary>
    public ValueTask<IList<WalSegmentInfo>> GetSegmentsAsync(CancellationToken ct = default)
    {
        return ValueTask.FromResult<IList<WalSegmentInfo>>(new List<WalSegmentInfo>());
    }

    /// <summary>
    ///     Disposes the manager.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _logger.LogDebug("No-op cloud WAL manager disposed");
        return ValueTask.CompletedTask;
    }
}
