using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;

namespace Gravel.Engine.Managers;

class WalManager : IAsyncDisposable, IDisposable
{
    readonly string _walDir;
    readonly IWalFactory _walFactory;

    public WalManager(IWalFactory walFactory, string walDir)
    {
        _walFactory = walFactory;
        _walDir = walDir;
        WalWriter = _walFactory.CreateWriter(_walDir);
    }

    public IWalWriter WalWriter { get; }

    public ulong LastSequence => WalWriter.LastSequence;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await WalWriter.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            WalWriter.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    public IWalReader CreateReader()
    {
        return _walFactory.CreateReader(_walDir);
    }

    public ValueTask BeginTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        return WalWriter.BeginTransactionAsync(txnId, ct);
    }

    public ValueTask AppendAsync(ulong txnId, DbEntry entry, CancellationToken ct = default)
    {
        return WalWriter.AppendAsync(txnId, entry, ct);
    }

    public ValueTask CommitTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        return WalWriter.CommitTransactionAsync(txnId, ct);
    }

    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        return WalWriter.FlushAsync(ct);
    }

    public async ValueTask ReplayAsync(Func<WalRecord, ValueTask> onRecord, CancellationToken ct = default)
    {
        await using var reader = _walFactory.CreateReader(_walDir);
        await foreach (var rec in reader.ReplayAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            await onRecord(rec).ConfigureAwait(false);
        }
    }

    public async ValueTask<IList<WalRecord>> ReadAllAsync(CancellationToken ct = default)
    {
        var list = new List<WalRecord>();
        await using var reader = _walFactory.CreateReader(_walDir);
        await foreach (var rec in reader.ReplayAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            list.Add(rec);
        }

        return list;
    }

    public async ValueTask WriteTransactionAsync(
        ulong txnId, IReadOnlyList<DbEntry> entries, bool flushOnCommit = false, CancellationToken ct = default)
    {
        await WalWriter.BeginTransactionAsync(txnId, ct).ConfigureAwait(false);
        try
        {
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                await WalWriter.AppendAsync(txnId, e, ct).ConfigureAwait(false);
            }

            await WalWriter.CommitTransactionAsync(txnId, ct).ConfigureAwait(false);
            if (flushOnCommit) await WalWriter.FlushAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await WalWriter.RollbackTransactionAsync(txnId, ct).ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }

    // Replay WAL and apply committed staged entries into the provided MemTableManager.
    public async ValueTask<int> ReplayIntoMemTableAsync(MemTableManager memTableManager, CancellationToken ct = default)
    {
        await using var reader = _walFactory.CreateReader(_walDir);
        var staging = new List<DbEntry>();
        var replayed = 0;
        await foreach (var rec in reader.ReplayAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            switch (rec.Type)
            {
                case WalConstants.RecordBeginTxn:
                    staging.Clear();
                    break;
                case WalConstants.RecordEntry:
                    if (rec.Entry.HasValue)
                    {
                        staging.Add(rec.Entry.Value);
                        replayed++;
                    }

                    break;
                case WalConstants.RecordCommitTxn:
                    // Apply all staged WAL entries atomically into the memtable via manager
                    memTableManager.ApplyStagedEntries(staging);
                    staging.Clear();
                    break;
                case WalConstants.RecordRollbackTxn:
                    staging.Clear();
                    break;
            }
        }

        return replayed;
    }
}
