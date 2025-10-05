namespace Gravel.Abstractions.Storage.Sst;

/// <summary>
///     Interface for reading entries from an SST file, including key lookup, full scan, and range tombstone access.
/// </summary>
public interface ISstReader : IAsyncInitializable, IDisposable
{
    /// <summary>
    ///     Asynchronously gets the entry for the specified key, or null if not found.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entry if found, otherwise null.</returns>
    ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Asynchronously reads all entries in the SST file.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of all entries.</returns>
    IAsyncEnumerable<DbEntry> ReadAllAsync(CancellationToken ct = default);

    /// <summary>
    ///     Asynchronously checks if the SST file might contain the specified key (e.g., via bloom filter).
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the key might be present, otherwise false.</returns>
    ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Gets the list of range tombstones (range deletes) contained in this SST file.
    /// </summary>
    /// <returns>A read-only list of tuples containing start key, end key, and sequence number.</returns>
    IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes();
}
