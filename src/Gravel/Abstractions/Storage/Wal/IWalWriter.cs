namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Write-Ahead Log interface: append-only storage for durability and recovery.
///     Supports transactional semantics (BEGIN, DATA, COMMIT, ROLLBACK).
/// </summary>
public interface IWalWriter : IAsyncDisposable
{
    /// <summary>
    ///     Sequence number of the last appended record.
    /// </summary>
    ulong LastSequence { get; }

    /// <summary>
    ///     Current segment size in bytes.
    /// </summary>
    long CurrentSize { get; }

    /// <summary>
    ///     Write a BEGIN record for the specified transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask BeginTransactionAsync(ulong txnId, CancellationToken ct = default);

    /// <summary>
    ///     Write a DATA record for the specified transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="entry">The database entry.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask AppendAsync(ulong txnId, DbEntry entry, CancellationToken ct = default);

    /// <summary>
    ///     Write a COMMIT record for the specified transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask CommitTransactionAsync(ulong txnId, CancellationToken ct = default);

    /// <summary>
    ///     Write a ROLLBACK record for the specified transaction.
    /// </summary>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask RollbackTransactionAsync(ulong txnId, CancellationToken ct = default);

    /// <summary>
    ///     Ensure all pending writes are durable on disk.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask FlushAsync(CancellationToken ct = default);
}
