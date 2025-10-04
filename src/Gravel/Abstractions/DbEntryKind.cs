namespace Gravel.Abstractions;

/// <summary>
///     Type of database entry.
/// </summary>
public enum DbEntryKind : byte
{
    Put = 0,
    DeleteKey = 1,
    DeleteRange = 2
}
