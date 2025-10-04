using System.Runtime.CompilerServices;
using Gravel.Abstractions.Storage.Wal;

namespace Gravel.Storage.InMemory.Wal;

public sealed class InMemoryWalReader(InMemoryWalWriter writer) : IWalReader
{
    public async IAsyncEnumerable<WalRecord> ReplayAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var (type, txn, entry) in writer.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            switch (type)
            {
                case WalConstants.RecordBeginTxn: yield return WalRecord.Begin(txn); break;
                case WalConstants.RecordCommitTxn: yield return WalRecord.Commit(txn); break;
                case WalConstants.RecordRollbackTxn: yield return WalRecord.Rollback(txn); break;
                case WalConstants.RecordEntry:
                    if (entry.HasValue) yield return WalRecord.DbEntry(txn, entry.Value);
                    break;
            }

            await Task.Yield();
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
