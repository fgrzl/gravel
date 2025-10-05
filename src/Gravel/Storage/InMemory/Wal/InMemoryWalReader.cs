using System.Runtime.CompilerServices;
using Gravel.Abstractions.Storage.Wal;

namespace Gravel.Storage.InMemory.Wal;

/// <summary>
///     In-memory WAL reader that replays records from an <see cref="InMemoryWalWriter"/> instance.
/// </summary>
/// <param name="writer">The in-memory WAL writer to read from.</param>
public sealed class InMemoryWalReader(InMemoryWalWriter writer) : IWalReader
{
    /// <summary>
    ///     Asynchronously replays WAL records from the in-memory writer.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of <see cref="WalRecord"/>.</returns>
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

    /// <summary>
    ///     Disposes the reader asynchronously. No-op for in-memory implementation.
    /// </summary>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
