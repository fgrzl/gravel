using System.Collections.Concurrent;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;

// ensure WalConstants

namespace Gravel.Storage.InMemory.Wal;

/// <summary>
///     In-memory implementation of <see cref="IWalWriter" /> for transactional Write-Ahead Log operations.
/// </summary>
/// <remarks>
///     Stores WAL records in a concurrent queue, supporting transactional semantics and sequence tracking.
/// </remarks>
public sealed class InMemoryWalWriter(int maxBufferedRecords = int.MaxValue) : IWalWriter
{
    readonly int _max = maxBufferedRecords <= 0 ? int.MaxValue : maxBufferedRecords;
    readonly ConcurrentQueue<(byte Type, ulong Txn, DbEntry? Entry)> _records = new();

    /// <inheritdoc />
    public ulong LastSequence { get; private set; }

    /// <inheritdoc />
    public long CurrentSize => _records.Count;

    /// <inheritdoc />
    public ValueTask BeginTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        Enqueue((WalConstants.RecordBeginTxn, txnId, null));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask AppendAsync(ulong txnId, DbEntry entry, CancellationToken ct = default)
    {
        var seq = entry.Sequence == 0 ? LastSequence + 1 : entry.Sequence;
        if (seq <= LastSequence) seq = LastSequence + 1;
        LastSequence = seq;
        var tagged = new DbEntry(entry.Kind, entry.Key, entry.Value, seq);
        Enqueue((WalConstants.RecordEntry, txnId, tagged));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask CommitTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        Enqueue((WalConstants.RecordCommitTxn, txnId, null));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RollbackTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        Enqueue((WalConstants.RecordRollbackTxn, txnId, null));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Enqueues a WAL record, maintaining the buffer size limit.
    /// </summary>
    /// <param name="rec">The WAL record to enqueue.</param>
    void Enqueue((byte Type, ulong Txn, DbEntry? Entry) rec)
    {
        _records.Enqueue(rec);
        while (_records.Count > _max && _records.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    ///     Returns a snapshot of all WAL records currently buffered.
    /// </summary>
    /// <returns>An enumerable of WAL records.</returns>
    internal IEnumerable<(byte Type, ulong Txn, DbEntry? Entry)> Snapshot()
    {
        return _records.ToArray();
    }
}
