namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Lightweight discriminated union of WAL records.
/// </summary>
public readonly record struct WalRecord(byte Type, ulong TxnId, DbEntry? Entry)
{
    public static WalRecord Begin(ulong txnId)
    {
        return new WalRecord(WalConstants.RecordBeginTxn, txnId, null);
    }

    public static WalRecord Commit(ulong txnId)
    {
        return new WalRecord(WalConstants.RecordCommitTxn, txnId, null);
    }

    public static WalRecord Rollback(ulong txnId)
    {
        return new WalRecord(WalConstants.RecordRollbackTxn, txnId, null);
    }

    public static WalRecord DbEntry(ulong txnId, DbEntry entry)
    {
        return new WalRecord(WalConstants.RecordEntry, txnId, entry);
    }
}