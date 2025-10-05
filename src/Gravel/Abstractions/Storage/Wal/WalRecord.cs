namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Lightweight discriminated union of WAL records, representing transactional operations and database entries in the
///     Write-Ahead Log.
/// </summary>
/// <param name="Type">The record type (begin, commit, rollback, entry).</param>
/// <param name="TxnId">The transaction ID associated with the record.</param>
/// <param name="Entry">The database entry, if applicable.</param>
public readonly record struct WalRecord(byte Type, ulong TxnId, DbEntry? Entry)
{
    /// <summary>
    ///     Creates a WAL record representing the beginning of a transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <returns>A WAL record for transaction begin.</returns>
    public static WalRecord Begin(ulong txnId)
    {
        return new WalRecord(WalConstants.RecordBeginTxn, txnId, null);
    }

    /// <summary>
    ///     Creates a WAL record representing the commit of a transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <returns>A WAL record for transaction commit.</returns>
    public static WalRecord Commit(ulong txnId)
    {
        return new WalRecord(WalConstants.RecordCommitTxn, txnId, null);
    }

    /// <summary>
    ///     Creates a WAL record representing the rollback of a transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <returns>A WAL record for transaction rollback.</returns>
    public static WalRecord Rollback(ulong txnId)
    {
        return new WalRecord(WalConstants.RecordRollbackTxn, txnId, null);
    }

    /// <summary>
    ///     Creates a WAL record representing a database entry within a transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="entry">The database entry.</param>
    /// <returns>A WAL record for a database entry.</returns>
    public static WalRecord DbEntry(ulong txnId, DbEntry entry)
    {
        return new WalRecord(WalConstants.RecordEntry, txnId, entry);
    }
}
