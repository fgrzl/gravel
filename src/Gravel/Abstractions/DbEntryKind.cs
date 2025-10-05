namespace Gravel.Abstractions;

/// <summary>
///     Type of database entry.
/// </summary>
public enum DbEntryKind : byte
{
    /// <summary>
    ///     Represents a put (insert/update) operation.
    /// </summary>
    Put = 0,

    /// <summary>
    ///     Represents a single-key delete operation.
    /// </summary>
    DeleteKey = 1,

    /// <summary>
    ///     Represents a range delete operation.
    /// </summary>
    DeleteRange = 2
}
