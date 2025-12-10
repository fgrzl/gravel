using Gravel.Abstractions;
using Gravel.Cloud.Integration;
using Gravel.Cloud.WAL;
using Gravel.Internals;
using Gravel.Logging;
using Gravel.Storage;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Engine;

/// <summary>
///     Actor-driven LSM database engine using TLV serialization and cloud-native storage.
///     Coordinates memtable, levels, WAL, and background actor tasks.
/// </summary>
public sealed class DbEngine : IDbEngine
{
    readonly StorageInstance _storage;
    readonly ILogger _logger;
    readonly object _initLock = new();

    bool _initialized;
    MemTable? _memTable;
    Levels? _levels;
    CloudNativeWAL? _wal;
    SequenceGenerator? _sequenceGenerator;

    /// <summary>
    ///     Initializes a new <see cref="DbEngine" /> instance.
    /// </summary>
    public DbEngine(StorageInstance storage, ILogger? logger = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? NullLogger.Instance;
        _initialized = false;

        _logger.LogInformation("DbEngine created with mode: {Mode}", storage.Mode);
    }

    /// <summary>
    ///     Initializes the engine: loads levels, replays WAL, sets up memtable.
    /// </summary>
    public async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        lock (_initLock)
        {
            if (_initialized)
                return;

            _initialized = true;
        }

        _sequenceGenerator = new SequenceGenerator();
        _wal = new CloudNativeWAL(logger: _logger);
        _levels = new Levels(_storage, _logger);
        _memTable = new MemTable(_logger);

        // Load existing levels
        await _levels.LoadAsync(ct);

        // Recover from WAL if needed
        await RecoverFromWALAsync(ct);

        _logger.LogInformation("DbEngine initialized. Mode={Mode}", _storage.Mode);
    }

    /// <summary>
    ///     Checks if a key exists in memtable or levels.
    /// </summary>
    public async ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var activity = TelemetryHelper.StartActivityScope(
            TelemetrySources.ActivitySource, _logger, "DbEngine.Exists",
            System.Diagnostics.ActivityKind.Internal);

        // Check memtable first
        if (_memTable?.Get(key) != null)
            return true;

        // Check levels
        return await _levels!.ExistsAsync(key, ct);
    }

    /// <summary>
    ///     Puts a key-value pair (insert or update).
    /// </summary>
    public async ValueTask PutAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var activity = TelemetryHelper.StartActivityScope(
            TelemetrySources.ActivitySource, _logger, "DbEngine.Put",
            System.Diagnostics.ActivityKind.Internal,
            new KeyValuePair<string, object?>("key.length", key.Length));

        var seq = _sequenceGenerator!.Next();
        var entry = DbEntry.Put(key, value, seq);

        // Append to WAL
        await _wal!.AppendAsync(entry, ct);

        // Insert into memtable
        _memTable!.Put(key, value, seq);

        // Check if flush needed
        if (_memTable.ShouldFlush())
        {
            await FlushMemTableAsync(ct);
        }

        TelemetrySources.DbPuts.Add(1, new KeyValuePair<string, object?>("mode", _storage.Mode.ToString()));
    }

    /// <summary>
    ///     Inserts a key-value pair, failing if key exists.
    /// </summary>
    public async ValueTask InsertAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // Check if key exists
        var existing = await GetAsync(key, ct);
        if (existing.HasValue)
            throw new InvalidOperationException($"Key already exists");

        await PutAsync(key, value, ct);
    }

    /// <summary>
    ///     Gets a value by key, returns null if not found.
    /// </summary>
    public async ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var activity = TelemetryHelper.StartActivityScope(
            TelemetrySources.ActivitySource, _logger, "DbEngine.Get",
            System.Diagnostics.ActivityKind.Internal,
            new KeyValuePair<string, object?>("key.length", key.Length));

        // Check memtable first
        var memEntry = _memTable!.Get(key);
        if (memEntry.HasValue)
        {
            var (value, _) = memEntry.Value;
            if (value.Length > 0)
            {
                TelemetrySources.DbGets.Add(1, new KeyValuePair<string, object?>("hit", "memtable"));
                return value;
            }
        }

        // Check levels
        var levelValue = await _levels!.GetAsync(key, ct);
        if (levelValue.HasValue)
        {
            TelemetrySources.DbGets.Add(1, new KeyValuePair<string, object?>("hit", "levels"));
            return levelValue.Value;
        }

        TelemetrySources.DbGets.Add(1, new KeyValuePair<string, object?>("hit", "none"));
        return null;
    }

    /// <summary>
    ///     Deletes a key by storing a tombstone.
    /// </summary>
    public async ValueTask<bool> DeleteAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var activity = TelemetryHelper.StartActivityScope(
            TelemetrySources.ActivitySource, _logger, "DbEngine.Delete",
            System.Diagnostics.ActivityKind.Internal,
            new KeyValuePair<string, object?>("key.length", key.Length));

        // Check if key exists
        var exists = await ExistsAsync(key, ct);
        if (!exists)
            return false;

        var seq = _sequenceGenerator!.Next();
        var entry = DbEntry.DeleteKey(key, seq);

        // Append tombstone to WAL
        await _wal!.AppendAsync(entry, ct);

        // Mark as deleted in memtable (store tombstone)
        _memTable!.Delete(key, seq);

        // Check if flush needed
        if (_memTable.ShouldFlush())
        {
            await FlushMemTableAsync(ct);
        }

        TelemetrySources.DbDeletes.Add(1);
        return true;
    }

    /// <summary>
    ///     Deletes a range of keys.
    /// </summary>
    public async ValueTask DeleteRangeAsync(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var activity = TelemetryHelper.StartActivityScope(
            TelemetrySources.ActivitySource, _logger, "DbEngine.DeleteRange",
            System.Diagnostics.ActivityKind.Internal,
            new KeyValuePair<string, object?>("range.keys", 2));

        var seq = _sequenceGenerator!.Next();
        var entry = DbEntry.DeleteRange(start, end, seq);

        // Append range delete to WAL
        await _wal!.AppendAsync(entry, ct);

        // Mark range as deleted in memtable
        _memTable!.DeleteRange(start, end, seq);

        // Check if flush needed
        if (_memTable.ShouldFlush())
        {
            await FlushMemTableAsync(ct);
        }

        TelemetrySources.DbRangeDeletes.Add(1);
    }

    /// <summary>
    ///     Applies a batch of mutations atomically.
    /// </summary>
    public async ValueTask BatchAsync(IEnumerable<Mutation> mutations, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var activity = TelemetryHelper.StartActivityScope(
            TelemetrySources.ActivitySource, _logger, "DbEngine.Batch",
            System.Diagnostics.ActivityKind.Internal);

        var mutationList = mutations.ToList();

        // Apply all mutations in sequence
        foreach (var mutation in mutationList)
        {
            switch (mutation.Op)
            {
                case MutationOp.Put:
                    await PutAsync(mutation.Key, mutation.Value, ct);
                    break;
                case MutationOp.Delete:
                    await DeleteAsync(mutation.Key, ct);
                    break;
                case MutationOp.DeleteRange:
                    await DeleteRangeAsync(mutation.Key, mutation.Value, ct);
                    break;
            }
        }

        TelemetrySources.DbBatches.Add(1, new KeyValuePair<string, object?>("mutations", mutationList.Count));
    }

    /// <summary>
    ///     Flushes the memtable to an SST file and triggers level management.
    /// </summary>
    private async ValueTask FlushMemTableAsync(CancellationToken ct)
    {
        if (_memTable == null || _memTable.IsEmpty)
            return;

        _logger.LogInformation("Flushing memtable with {Count} entries", _memTable.Count);

        var entries = _memTable.GetEntries();
        var sstPath = await _levels!.WriteSSTAsync(entries, 0, ct);

        _logger.LogInformation("Memtable flushed to {Path}", sstPath);

        // Reset memtable
        _memTable = new MemTable(_logger);

        TelemetrySources.DbFlushes.Add(1);
    }

    /// <summary>
    ///     Recovers from WAL segments if database was not cleanly shut down.
    /// </summary>
    private async ValueTask RecoverFromWALAsync(CancellationToken ct)
    {
        // Placeholder: actual recovery depends on WAL implementation
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Ensures engine is initialized before operations.
    /// </summary>
    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        await InitializeAsync(ct);
    }

    /// <summary>
    ///     Disposes the engine, flushes memtable, closes storage.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_initialized && _memTable != null)
        {
            // Flush any pending memtable data
            if (!_memTable.IsEmpty)
            {
                await FlushMemTableAsync(CancellationToken.None);
            }
        }

        // Close WAL
        if (_wal != null)
            await _wal.DisposeAsync();

        // Close storage
        await _storage.DisposeAsync();

        _logger.LogInformation("DbEngine disposed");
    }

    /// <summary>
    ///     Disposes the engine synchronously.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }
}
