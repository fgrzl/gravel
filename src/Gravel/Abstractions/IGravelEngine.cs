namespace Gravel.Abstractions;

public interface IGravelEngine : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Initialize engine (load SST levels, replay WAL). Safe to call multiple times.
    ///     Other API methods will auto-call this lazily, but explicit call can surface errors early.
    /// </summary>
    ValueTask InitializeAsync(CancellationToken ct = default);

    ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);
    ValueTask PutAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken ct = default);
    ValueTask InsertAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken ct = default);
    ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);
    ValueTask<bool> DeleteAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);
    ValueTask DeleteRangeAsync(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end, CancellationToken ct = default);

    /// <summary>
    ///     Executes a group of operations atomically as a single commit.
    ///     Equivalent to staging all mutations in a transaction and committing.
    /// </summary>
    ValueTask BatchAsync(IEnumerable<Mutation> mutations, CancellationToken ct = default);

    IAsyncEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> ScanAsync(Query query,
        CancellationToken ct = default);

    ValueTask<IGravelTransaction> BeginTransactionAsync(CancellationToken ct = default);
}