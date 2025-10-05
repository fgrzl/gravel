namespace Gravel.Abstractions.Storage.Sst;

/// <summary>
///     Interface for writing a sequence of database entries to an SST file in sorted order.
/// </summary>
public interface ISstWriter : IAsyncInitializable, IAsyncDisposable
{
    /// <summary>
    ///     Writes a sequence of database entries (puts and tombstones) in sorted order.
    ///     Caller must ensure entries are ordered by key ascending, then by desired precedence (newest first per key).
    /// </summary>
    /// <param name="entries">The async sequence of database entries to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    ValueTask WriteAsync(IAsyncEnumerable<DbEntry> entries, CancellationToken ct = default);

    /// <summary>
    ///     Flushes any buffered data to the SST file. This operation is idempotent.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous flush operation.</returns>
    ValueTask FlushAsync(CancellationToken ct = default);
}
