namespace Gravel.Abstractions;

/// <summary>
///     Represents a scan query for the database.
/// </summary>
public readonly record struct Query(
    ReadOnlyMemory<byte>? Start = null,
    ReadOnlyMemory<byte>? End = null,
    int? Limit = null);