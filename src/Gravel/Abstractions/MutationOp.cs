namespace Gravel.Abstractions;

/// <summary>
///     Operation type staged in a transaction.
/// </summary>
public enum MutationOp : byte
{
    Put,
    Insert,
    Delete,
    DeleteRange
}