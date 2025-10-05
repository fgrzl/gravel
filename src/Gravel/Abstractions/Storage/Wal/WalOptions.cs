namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Base options for configuring Write-Ahead Log (WAL) storage.
/// </summary>
public abstract record WalOptions
{
    /// <summary>
    ///     The kind of WAL implementation (e.g., file, memory).
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    ///     The segment size for WAL records (format may vary by implementation).
    /// </summary>
    public required string SegmentSize { get; set; }
}
