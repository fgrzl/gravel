namespace Gravel.Abstractions;

/// <summary>
///     Represents a lightweight, single-writer transaction in the Gravel engine.
///     Buffers operations until commit, providing atomic all-or-nothing semantics.
///     Multiple transactions can begin concurrently, but commits are serialized.
/// </summary>
public interface IGravelTransaction : IAsyncDisposable
{
    /// <summary>
    ///     Snapshot sequence when this transaction began (read view).
    /// </summary>
    ulong BeginSequence { get; }

    /// <summary>
    ///     Global sequence assigned on successful commit, or null if uncommitted.
    /// </summary>
    ulong? CommitSequence { get; }

    /// <summary>
    ///     Checks if a key exists in the transaction's view.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the key exists, otherwise false.</returns>
    ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Stage a put (insert/update) operation within the transaction.
    ///     Optional TTL will be enforced at query time and during compaction.
    /// </summary>
    /// <param name="key">The key to put.</param>
    /// <param name="value">The value to associate.</param>
    /// <param name="ttl">Optional time-to-live.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask PutAsync(
        ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Stage an insert operation within the transaction.
    ///     Insert fails at commit if the key already exists in the snapshot view.
    /// </summary>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value to associate.</param>
    /// <param name="ttl">Optional time-to-live.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask InsertAsync(
        ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Stage a delete operation within the transaction (written as a tombstone).
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask DeleteAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Stage a range delete operation within the transaction.
    ///     Covers the half-open interval [start, end).
    /// </summary>
    /// <param name="start">The start key (inclusive).</param>
    /// <param name="end">The end key (exclusive).</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask DeleteRangeAsync(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end, CancellationToken ct = default);

    /// <summary>
    ///     Attempt to read within the transaction's view.
    ///     Returns staged writes first, otherwise snapshot data as of BeginSequence.
    /// </summary>
    /// <param name="key">The key to retrieve.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The value, or null if not found.</returns>
    ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Commit all staged operations atomically.
    ///     Operations are validated (including inserts), appended to the WAL,
    ///     and applied to the MemTable.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    ValueTask CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Roll back all staged operations.
    ///     Nothing is applied to WAL or MemTable.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RollbackAsync(CancellationToken ct = default);
}
