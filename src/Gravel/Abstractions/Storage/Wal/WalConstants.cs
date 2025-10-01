namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Shared record type constants.
/// </summary>
public static class WalConstants
{
    public const byte RecordBeginTxn = 0x01;
    public const byte RecordCommitTxn = 0x02;
    public const byte RecordRollbackTxn = 0x03;
    public const byte RecordEntry = 0x04;
}