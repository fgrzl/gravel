namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Shared record type constants for Write-Ahead Log (WAL) operations.
/// </summary>
public static class WalConstants
{
    /// <summary>
    ///     WAL record type for beginning a transaction.
    /// </summary>
    public const byte RecordBeginTxn = 0x01;

    /// <summary>
    ///     WAL record type for committing a transaction.
    /// </summary>
    public const byte RecordCommitTxn = 0x02;

    /// <summary>
    ///     WAL record type for rolling back a transaction.
    /// </summary>
    public const byte RecordRollbackTxn = 0x03;

    /// <summary>
    ///     WAL record type for a database entry within a transaction.
    /// </summary>
    public const byte RecordEntry = 0x04;
}
