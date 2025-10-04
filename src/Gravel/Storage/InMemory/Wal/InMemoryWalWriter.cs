using System.Collections.Concurrent;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;

// ensure WalConstants

namespace Gravel.Storage.InMemory.Wal;

public sealed class InMemoryWalWriter(int maxBufferedRecords = int.MaxValue) : IWalWriter
{
    readonly int _max = maxBufferedRecords <= 0 ? int.MaxValue : maxBufferedRecords;
    readonly ConcurrentQueue<(byte Type, ulong Txn, DbEntry? Entry)> _records = new();
    public ulong LastSequence { get; private set; }
    public long CurrentSize => _records.Count;

    public ValueTask BeginTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        Enqueue((WalConstants.RecordBeginTxn, txnId, null));
        return ValueTask.CompletedTask;
    }

    public ValueTask AppendAsync(ulong txnId, DbEntry entry, CancellationToken ct = default)
    {
        var seq = entry.Sequence == 0 ? LastSequence + 1 : entry.Sequence;
        if (seq <= LastSequence) seq = LastSequence + 1;
        LastSequence = seq;
        var tagged = new DbEntry(entry.Kind, entry.Key, entry.Value, seq);
        Enqueue((WalConstants.RecordEntry, txnId, tagged));
        return ValueTask.CompletedTask;
    }

    public ValueTask CommitTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        Enqueue((WalConstants.RecordCommitTxn, txnId, null));
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        Enqueue((WalConstants.RecordRollbackTxn, txnId, null));
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    void Enqueue((byte Type, ulong Txn, DbEntry? Entry) rec)
    {
        _records.Enqueue(rec);
        while (_records.Count > _max && _records.TryDequeue(out _))
        {
        }
    }

    internal IEnumerable<(byte Type, ulong Txn, DbEntry? Entry)> Snapshot()
    {
        return _records.ToArray();
    }
}
