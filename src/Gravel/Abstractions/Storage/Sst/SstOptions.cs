namespace Gravel.Abstractions.Storage.Sst;

/// <summary>
///     Base options for configuring SST (Sorted String Table) storage.
/// </summary>
public abstract record SstOptions
{
    /// <summary>
    ///     The kind of SST implementation (e.g., file, memory).
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    ///     The interval for sparse indexing within the SST.
    /// </summary>
    public required int SparseInterval { get; init; }
}
