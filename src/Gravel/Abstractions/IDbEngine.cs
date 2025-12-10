namespace Gravel.Abstractions;

/// <summary>
///     Main interface for the Gravel database engine. Provides asynchronous CRUD and batch operations.
///     All methods are thread-safe.
/// </summary>
public interface IDbEngine : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Initializes the engine. Safe to call multiple times.
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
    /// </summary>
    /// <param name="mutations">The mutations to apply.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask BatchAsync(IEnumerable<Mutation> mutations, CancellationToken ct = default);
}
