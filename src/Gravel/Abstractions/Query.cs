namespace Gravel.Abstractions;

/// <summary>
///     Represents a scan query for the database.
/// </summary>
/// <param name="Start">
///     Optional. The start range of the query.
///     When not specified, the query will include records starting from the beginning.
/// </param>
/// <param name="End">
///     Optional. The end range of the query.
///     When not specified, the query will include records up to the end.
/// </param>
/// <param name="Limit">
///     Optional. The maximum number of records to return.
///     When not specified, there is no limit on the number of records.
/// </param>
public readonly record struct Query(
    ReadOnlyMemory<byte>? Start = null,
    ReadOnlyMemory<byte>? End = null,
    int? Limit = null);
