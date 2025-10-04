using Gravel.Engine;

namespace Gravel.Abstractions;

public interface IDbEngine : IAsyncDisposable, IDisposable
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

    IAsyncEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> ScanAsync(
        Query query,
        CancellationToken ct = default);

    ValueTask<IGravelTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    ///     Trigger compaction scheduling and let background compaction worker process passes until
    ///     no immediate compaction candidates remain. This is a manual request and will schedule
    ///     compaction tasks similarly to the automatic background triggers.
    /// </summary>
    ValueTask CompactAsync(CancellationToken ct = default);

    /// <summary>
    ///     Create a full backup archive (tar.gz) containing SST and WAL files plus a manifest.
    ///     The archive is written to the provided destination path. The backup represents a
    ///     point-in-time snapshot as of when this method acquires a short internal snapshot lock.
    /// </summary>
    ValueTask BackupAsync(string destinationPath, BackupOptions? options = null, CancellationToken ct = default);

    /// <summary>
    ///     Restore from a full backup archive. This operation is intended to be used on an offline
    ///     target and may throw if attempted against a live engine instance.
    /// </summary>
    ValueTask RestoreAsync(string archivePath, RestoreOptions? options = null, CancellationToken ct = default);
}
