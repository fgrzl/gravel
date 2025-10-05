using Gravel.Engine;

namespace Gravel.Abstractions;

/// <summary>
///     Main interface for the Gravel database engine. Provides asynchronous CRUD, batch, transaction,
///     compaction, backup, and restore operations. All methods are thread-safe.
/// </summary>
public interface IDbEngine : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Initializes the engine (loads SST levels, replays WAL). Safe to call multiple times.
    ///     Other API methods will auto-call this lazily, but explicit call can surface errors early.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous initialization.</returns>
    ValueTask InitializeAsync(CancellationToken ct = default);

    /// <summary>
    ///     Checks if a key exists in the database.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the key exists, otherwise false.</returns>
    ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Inserts or updates a key-value pair in the database.
    /// </summary>
    /// <param name="key">The key to put.</param>
    /// <param name="value">The value to associate.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask PutAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken ct = default);

    /// <summary>
    ///     Inserts a key-value pair, failing if the key already exists.
    /// </summary>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value to associate.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask InsertAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken ct = default);

    /// <summary>
    ///     Gets the value for a key, or null if not found.
    /// </summary>
    /// <param name="key">The key to retrieve.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The value, or null if not found.</returns>
    ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Deletes a key from the database.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the key was deleted, otherwise false.</returns>
    ValueTask<bool> DeleteAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    /// <summary>
    ///     Deletes a range of keys from the database.
    /// </summary>
    /// <param name="start">The start key (inclusive).</param>
    /// <param name="end">The end key (exclusive).</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask DeleteRangeAsync(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end, CancellationToken ct = default);

    /// <summary>
    ///     Executes a group of operations atomically as a single commit.
    ///     Equivalent to staging all mutations in a transaction and committing.
    /// </summary>
    /// <param name="mutations">The mutations to apply.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask BatchAsync(IEnumerable<Mutation> mutations, CancellationToken ct = default);

    /// <summary>
    ///     Scans the database using a query, yielding key-value pairs.
    /// </summary>
    /// <param name="query">The scan query.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of key-value pairs.</returns>
    IAsyncEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> ScanAsync(
        Query query,
        CancellationToken ct = default);

    /// <summary>
    ///     Begins a new transaction.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="IGravelTransaction" /> instance.</returns>
    ValueTask<IGravelTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    ///     Triggers compaction scheduling and lets the background compaction worker process passes until
    ///     no immediate compaction candidates remain. Manual request; schedules compaction tasks similarly
    ///     to automatic background triggers.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    ValueTask CompactAsync(CancellationToken ct = default);

    /// <summary>
    ///     Creates a full backup archive (tar.gz) containing SST and WAL files plus a manifest.
    ///     The archive is written to the provided destination path. The backup represents a
    ///     point-in-time snapshot as of when this method acquires a short internal snapshot lock.
    /// </summary>
    /// <param name="destinationPath">The path to write the backup archive.</param>
    /// <param name="options">Optional backup options.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask BackupAsync(string destinationPath, BackupOptions? options = null, CancellationToken ct = default);

    /// <summary>
    ///     Restores from a full backup archive. Intended for use on an offline target and may throw if
    ///     attempted against a live engine instance.
    /// </summary>
    /// <param name="archivePath">The path to the backup archive.</param>
    /// <param name="options">Optional restore options.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RestoreAsync(string archivePath, RestoreOptions? options = null, CancellationToken ct = default);
}
