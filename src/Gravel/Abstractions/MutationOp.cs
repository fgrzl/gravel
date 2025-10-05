namespace Gravel.Abstractions;

/// <summary>
///     Operation type staged in a transaction.
/// </summary>
public enum MutationOp : byte
{
    /// <summary>
    ///     Insert or update a key-value pair.
    /// </summary>
    Put = 0,

    /// <summary>
    ///     Insert a key-value pair, failing if the key exists.
    /// </summary>
    Insert = 1,

    /// <summary>
    ///     Delete a single key.
    /// </summary>
    Delete = 2,

    /// <summary>
    ///     Delete a range of keys.
    /// </summary>
    DeleteRange = 3
}
