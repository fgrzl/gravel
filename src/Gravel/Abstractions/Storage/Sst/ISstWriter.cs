namespace Gravel.Abstractions.Storage.Sst;

public interface ISstWriter : IAsyncDisposable
{
    /// <summary>
    ///     Write a sequence of database entries (puts and tombstones) in sorted order.
    ///     Caller must ensure entries are ordered by key asc, then by desired precedence (newest first per key).
    /// </summary>
    ValueTask WriteAsync(IAsyncEnumerable<DbEntry> entries, CancellationToken ct = default);

    /// <summary>
    ///     Flush any buffered data (idempotent).
    /// </summary>
    ValueTask FlushAsync(CancellationToken ct = default);
}