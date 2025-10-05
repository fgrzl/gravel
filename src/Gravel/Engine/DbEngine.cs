using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Engine.Managers;
using Gravel.Exceptions;
using Gravel.Internals;
using Gravel.Internals.Compaction;
using Gravel.Logging;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gravel.Engine;

/// <summary>
///     The main database engine for Gravel, providing LSM storage, transactional operations, compaction, backup, and
///     restore.
/// </summary>
public class DbEngine : IDbEngine
{
    readonly BackupManager _backupManager;
    readonly SemaphoreSlim _commitGate = new(1, 1);
    readonly ICompactionWorker _compactionWorker;
    readonly SemaphoreSlim _initGate = new(1, 1);
    readonly Levels _levels;
    readonly ILogger<DbEngine> _logger;
    readonly MemTableManager _memTableManager = new();
    readonly GravelOptions _options;
    readonly string _sstDir;
    readonly ISstFactory _sstFactory;
    readonly SstManager _sstManager;
    readonly TransactionManager _txnManager;
    readonly string _walDir;
    readonly WalManager _walManager;
    readonly IWalWriter _walWriter;

    bool _disposed;
    bool _initialized;
    long _nextTxnId;
    ulong _seq; // in-memory sequence allocator base

    /// <summary>
    ///     Initializes a new instance of the <see cref="DbEngine" /> class.
    /// </summary>
    /// <param name="options">The database options.</param>
    /// <param name="walFactory">The WAL factory.</param>
    /// <param name="sstFactory">The SST factory.</param>
    /// <param name="compactionWorker">The compaction worker.</param>
    /// <param name="logger">The logger.</param>
    public DbEngine(
        IOptions<GravelOptions> options,
        IWalFactory walFactory,
        ISstFactory sstFactory,
        ICompactionWorker compactionWorker,
        ILogger<DbEngine> logger)
    {
        _options = options.Value;

        _walDir = string.IsNullOrEmpty(_options.WalPath)
            ? Path.Combine(_options.DatabasePath, "wal")
            : _options.WalPath!;

        // Store factories and logger
        _sstFactory = sstFactory ?? throw new ArgumentNullException(nameof(sstFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _walManager = new WalManager(walFactory, _walDir);
        _walWriter = _walManager.WalWriter;

        _sstDir = string.IsNullOrEmpty(_options.SstPath)
            ? Path.Combine(_options.DatabasePath, "sst")
            : _options.SstPath!;
        var levelCount = Math.Max(1, _options.SstLevels <= 0 ? 7 : _options.SstLevels);
        _levels = new Levels(levelCount);

        _compactionWorker = compactionWorker;
        Log.EngineCreated(_logger, _options.DatabasePath, _walDir, _sstDir, levelCount);

        _backupManager = new BackupManager(_walWriter, _levels, _sstDir, _walDir, _logger);

        // Transaction manager orchestrates commits and memtable application.
        _txnManager = new TransactionManager(
            _memTableManager,
            _levels,
            _walWriter,
            _options,
            _commitGate,
            _logger,
            () => NextSeq(ref _seq),
            () => (ulong)Interlocked.Increment(ref _nextTxnId),
            FlushMemTableAsync);

        // SstManager consolidates direct interactions with SST readers/writers
        _sstManager = new SstManager(sstFactory, _levels, _sstDir, _logger);
    }

    /// <summary>
    ///     Initializes the engine (loads SST levels, replays WAL). Safe to call multiple times.
    ///     Other API methods will auto-call this lazily, but explicit call can surface errors early.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var act = TelemetryHelper.StartActivityScope(TelemetrySources.ActivitySource, _logger,
                "DbEngine.Initialize",
                ActivityKind.Internal,
                new KeyValuePair<string, object?>("db.path", _options.DatabasePath),
                new KeyValuePair<string, object?>("wal.dir", _walDir),
                new KeyValuePair<string, object?>("sst.dir", _sstDir));

            Log.DbOpening(_logger, _options.DatabasePath);
            // Delegate loading existing SST files to the SstManager
            await _sstManager.LoadExistingAsync(ct).ConfigureAwait(false);

            // Replay WAL into memtable using WalManager to keep DbEngine slim
            var replayed = await _walManager.ReplayIntoMemTableAsync(_memTableManager, ct).ConfigureAwait(false);
            TelemetrySources.WalReplayed.Add(replayed);
            Log.WalReplayedDetailed(_logger, replayed, _memTableManager.MemTable.Count);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    ///     Checks if a key exists in the database, considering range tombstones and SST lookups.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the key exists; otherwise, false.</returns>
    public async ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        // Delegate to GetAsync so range tombstones and SST lookups are applied consistently
        var got = await GetAsync(key, ct);
        return got.HasValue;
    }

    /// <summary>
    ///     Puts (inserts or updates) a key-value pair in the database.
    /// </summary>
    /// <param name="key">The key to put.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask PutAsync(
        ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.PutCalled(_logger, key.Length);
        await CommitSingleAsync(Mutation.Put(key, value), ct);
    }

    /// <summary>
    ///     Inserts a key-value pair in the database. Fails if the key already exists.
    /// </summary>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask InsertAsync(
        ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.InsertCalled(_logger, key.Length);
        await CommitSingleAsync(Mutation.Insert(key, value), ct);
    }

    /// <summary>
    ///     Gets the value for a key, considering memtable, range tombstones, and SSTs.
    /// </summary>
    /// <param name="key">The key to retrieve.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The value for the key, or null if not found.</returns>
    public async ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // Find latest range tombstone in memtable covering this key using range index (O(log n))
        _memTableManager.GetMemTable().TryGetCoveringRange(key.Span, out var coveringRangeSeq);

        // Check memtable direct entry for this key using helper that respects covering range sequence
        var mtValue = _memTableManager.TryGetFromMemTable(key, coveringRangeSeq);
        if (mtValue.HasValue) return mtValue.Value;

        // If covered by a memtable range tombstone and no newer memtable PUT, mask immediately
        if (coveringRangeSeq > 0) return null;

        // Otherwise, check SSTs: pick highest-seq entry across all files
        DbEntryKind bestKind = 0;
        ulong bestSeq = 0;
        ReadOnlyMemory<byte> bestValue = default;
        var haveCandidate = false;

        var lvlSnap = _levels.SnapshotLevels();
        for (var l = 0; l < lvlSnap.Count; l++)
        {
            var files = lvlSnap[l];
            if (files.Count == 0) continue;

            // For L0, newer files are appended; search in reverse to find newer entries earlier.
            var indices = l == 0 ? Enumerable.Range(0, files.Count).Reverse() : Enumerable.Range(0, files.Count);
            foreach (var idx in indices)
            {
                ct.ThrowIfCancellationRequested();
                var f = files[idx];

                // Bloom/filter check first if available
                if (!await f.Reader.MightContainAsync(key, ct).ConfigureAwait(false))
                    continue;

                var e = await f.Reader.GetAsync(key, ct).ConfigureAwait(false);
                if (e.HasValue)
                {
                    var entry = e.Value;
                    if (!haveCandidate || entry.Sequence > bestSeq)
                    {
                        bestSeq = entry.Sequence;
                        bestKind = entry.Kind;
                        bestValue = entry.Value;
                        haveCandidate = true;
                    }
                    else if (entry.Sequence == bestSeq)
                    {
                        // Tie-breaker: prefer delete over put to be conservative
                        if (entry.Kind == DbEntryKind.DeleteKey && bestKind == DbEntryKind.Put)
                        {
                            bestKind = entry.Kind;
                            bestValue = default;
                            haveCandidate = true;
                        }
                    }
                }

                // Even if no exact entry, a newer range tombstone in this file may mask lower seq puts
                var ranges = f.Reader.GetRangeDeletes();
                foreach (var (rs, re, rSeq) in ranges)
                    if (ByteComparer.Compare(rs.Span, key.Span) <= 0 && ByteComparer.Compare(key.Span, re.Span) < 0)
                        if (!haveCandidate || rSeq > bestSeq)
                        {
                            bestSeq = rSeq;
                            bestKind = DbEntryKind.DeleteRange;
                            bestValue = default;
                            haveCandidate = true;
                        }
            }
        }

        if (haveCandidate)
        {
            if (bestKind != DbEntryKind.Put)
                return null;

            Log.GetHitSst(_logger, key.Length);
            return bestValue;
        }

        Log.GetMiss(_logger, key.Length);
        return null;
    }

    /// <summary>
    ///     Deletes a key from the database.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the key existed and was deleted; otherwise, false.</returns>
    public async ValueTask<bool> DeleteAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.DeleteCalled(_logger, key.Length);
        return await CommitSingleAsync(Mutation.Delete(key), ct);
    }

    /// <summary>
    ///     Deletes a range of keys from the database.
    /// </summary>
    /// <param name="start">The start of the range (inclusive).</param>
    /// <param name="end">The end of the range (exclusive).</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask DeleteRangeAsync(
        ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.DeleteRangeCalled(_logger, start.Length, end.Length);
        await CommitSingleAsync(Mutation.DeleteRange(start, end), ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes a group of mutations atomically as a single commit.
    /// </summary>
    /// <param name="mutations">The mutations to commit.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask BatchAsync(IEnumerable<Mutation> mutations, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var list = mutations as IList<Mutation> ?? [.. mutations];
        if (list.Count == 0) return;
        Log.BatchCalled(_logger, list.Count);
        await CommitMutationsAsync(list, ct);
    }

    /// <summary>
    ///     Scans the database for key-value pairs matching the query.
    /// </summary>
    /// <param name="query">The scan query.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of key-value pairs.</returns>
    public async IAsyncEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> ScanAsync(
        Query query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        using var act = TelemetrySources.ActivitySource.StartActivity("Scan");
        act?.SetTag("scan.start", query.Start?.Length ?? -1);
        act?.SetTag("scan.end", query.End?.Length ?? -1);
        act?.SetTag("scan.limit", query.Limit ?? -1);

        Log.ScanCalled(_logger, query.Start?.Length, query.End?.Length, query.Limit);
        var sources = new List<IScanSource>
            { new MemTableScanSource(_memTableManager.GetMemTable(), 0, query.Start, query.End) };
        var lvlSnap = _levels.SnapshotLevels();

        for (var l = 0; l < lvlSnap.Count; l++)
            foreach (var f in lvlSnap[l])
                sources.Add(await SstScanSource.CreateAsync(f.Reader, l + 1, query.Start, query.End, ct));
        foreach (var item in MergeSources(sources, query, ct))
        {
            Log.ScanYielding(_logger, item.Key.Length);
            yield return item;
        }
    }

    /// <summary>
    ///     Begins a new transaction.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A new transaction instance.</returns>
    public async ValueTask<IGravelTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var begin = _walManager.LastSequence;
        var id = (ulong)Interlocked.Increment(ref _nextTxnId);
        Log.BeginTransactionCreated(_logger, id, begin);
        return new Transaction(this, id, begin);
    }

    /// <summary>
    ///     Disposes the database engine and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Disposed(_logger);
        _walManager.Dispose();
        _commitGate.Dispose();
        _initGate.Dispose();
        var snapshot = _levels.SnapshotLevels();
        foreach (var lvl in snapshot)
            foreach (var f in lvl)
                f.Reader.Dispose();

        try
        {
            _compactionWorker.Dispose();
        }
        catch
        {
        }
    }

    /// <summary>
    ///     Asynchronously disposes the database engine and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Disposed(_logger);
        await _walManager.DisposeAsync();
        _commitGate.Dispose();
        _initGate.Dispose();
        var snapshot = _levels.SnapshotLevels();
        foreach (var lvl in snapshot)
            foreach (var f in lvl)
                f.Reader.Dispose();

        try
        {
            _compactionWorker.Dispose();
        }
        catch
        {
        }
    }

    /// <summary>
    ///     Triggers manual compaction scheduling and lets the background worker process passes until no immediate candidates
    ///     remain.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask CompactAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var act = TelemetrySources.ActivitySource.StartActivity("Compaction.Manual");
        act?.SetTag("requested", true);

        // Keep scheduling passes until there are no immediate candidates or until we tried a reasonable number of times.
        // Limit to LevelCount-1 iterations to avoid unbounded looping.
        for (var pass = 0; pass < Math.Max(1, _levels.LevelCount - 1); pass++)
        {
            ct.ThrowIfCancellationRequested();
            var didSchedule = await TryScheduleCompactionPassAsync(ct).ConfigureAwait(false);
            if (!didSchedule) break;
        }

        // scheduling performed; background worker will process enqueued tasks
    }

    /// <summary>
    ///     Creates a full backup archive (tar.gz) containing SST and WAL files plus a manifest.
    /// </summary>
    /// <param name="destinationPath">The destination path for the backup archive.</param>
    /// <param name="options">Backup options.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask BackupAsync(
        string destinationPath, BackupOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new BackupOptions();
        await EnsureInitializedAsync(ct);

        await _backupManager.BackupAsync(destinationPath, options, _commitGate, FlushMemTableAsync, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Restores the database from a full backup archive.
    /// </summary>
    /// <param name="archivePath">The path to the backup archive.</param>
    /// <param name="options">Restore options.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask RestoreAsync(
        string archivePath, RestoreOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new RestoreOptions();

        if (options.RequireEngineStopped && !_disposed && _initialized)
            throw new GravelInvalidOperationException("Restore requires the engine to be offline or uninitialized.");

        await _backupManager.RestoreAsync(archivePath, options, _options.DatabasePath, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Atomically allocates the next sequence number for versioning.
    /// </summary>
    /// <param name="seq">A reference to the sequence number.</param>
    /// <returns>The next sequence value.</returns>
    static ulong NextSeq(ref ulong seq)
    {
        ulong current, next;
        do
        {
            current = seq;
            next = current + 1L;
        } while (Interlocked.CompareExchange(ref seq, next, current) != current);

        return next;
    }

    async ValueTask MaybeFlushAsync(CancellationToken ct)
    {
        if (_memTableManager.MemTable.Count < _options.MemTableThreshold) return;
        Log.FlushTrigger(_logger, _memTableManager.MemTable.Count, _options.MemTableThreshold);
        await FlushMemTableAsync(ct).ConfigureAwait(false);
    }

    async ValueTask FlushMemTableAsync(CancellationToken ct)
    {
        var seqTag = _walManager.LastSequence;
        var mt = _memTableManager.GetMemTable();

        using var act = TelemetryHelper.StartActivityScope(TelemetrySources.ActivitySource, _logger,
            "Flush.MemTable");
        act?.SetTag("entries", mt.Count);

        Log.FlushEnqueued(_logger, mt.Count);

        // Delegate SST write/reader installation to SstManager
        var sst = await _sstManager.WriteMemTableAsync(mt, seqTag, ct).ConfigureAwait(false);

        _memTableManager.ResetMemTable();

        TelemetrySources.Flushes.Add(1);
        TelemetrySources.FlushSize.Record(mt.Count);
        act?.SetTag("sst.path", sst.Path);

        await MaybeCompactAsync(ct).ConfigureAwait(false);
    }

    async ValueTask MaybeCompactAsync(CancellationToken ct)
    {
        // Try to schedule a single compaction pass if thresholds are met. Keep behavior conservative: schedule at most one pass.
        await TryScheduleCompactionPassAsync(ct).ConfigureAwait(false);
    }

    async Task<bool> TryScheduleCompactionPassAsync(CancellationToken ct)
    {
        for (var l = 0; l < _levels.LevelCount - 1; l++)
        {
            if (ct.IsCancellationRequested) return false;
            if (!_levels.MeetsFanIn(l, _options.CompactionFanInThreshold)) continue;

            var snapshot = _levels.SnapshotLevels();
            var to = snapshot[l].ToList(); // don't remove yet; keep visible until success
            if (to.Count == 0) continue;
            var next = l + 1;
            var outDir = Path.Combine(_sstDir, $"L{next}");
            var outPath = Path.Combine(outDir, $"{DateTime.UtcNow.Ticks:D20}.sst");

            using var act = TelemetrySources.ActivitySource.StartActivity("Compaction.Pass");
            act?.SetTag("from_level", l);
            act?.SetTag("file_count", to.Count);
            act?.SetTag("to_level", next);

            Log.CompactionStarted(_logger, l, to.Count, next);

            // Enqueue compaction task instead of doing inline merge
            var task = new MergeFilesCompactionTask(to, outPath, _sstFactory,
                Compactor.EstimateMergedCount(to), async (p, inputs) =>
                {
                    // callback invoked on task success: install new SST and remove inputs atomically
                    var newR = await _sstManager.CreateReaderAsync(p, ct).ConfigureAwait(false);
                    _levels.ApplyCompaction(l, inputs, next, new SstFile(p, newR, 0));

                    foreach (var f in inputs)
                    {
                        try
                        {
                            f.Reader.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Log.SstDisposeError(_logger, f.Path, ex.Message);
                        }

                        try
                        {
                            File.Delete(f.Path);
                        }
                        catch (Exception ex)
                        {
                            Log.SstDeleteFailed(_logger, f.Path, ex.Message);
                        }
                    }

                    TelemetrySources.Compactions.Add(1);
                    Log.CompactionFinished(_logger, p);
                }, _logger);

            await _compactionWorker.EnqueueAsync(task, ct).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    static IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> MergeSources(
        List<IScanSource> sources,
        Query query, CancellationToken ct)
    {
        sources = [.. sources.Where(s => s.HasItem)];
        var cmp = Comparer<IScanSource>.Create((a, b) =>
        {
            var c = ByteComparer.Compare(a.Key, b.Key);
            if (c != 0) return c;
            // For same key, newer sequence first; if equal, lower precedence (memtable before SSTs)
            var seqCmp = b.Sequence.CompareTo(a.Sequence);
            return seqCmp != 0 ? seqCmp : a.Precedence.CompareTo(b.Precedence);
        });

        var activeRanges = new List<(byte[] Start, byte[] End, ulong Seq)>();
        var produced = 0;

        while (sources.Count > 0)
        {
            if (ct.IsCancellationRequested)
                yield break; // end enumeration gracefully on cancellation

            sources.Sort(cmp);
            var currentKey = sources[0].Key;

            // prune expired ranges (end <= currentKey)
            activeRanges.RemoveAll(r => ByteComparer.Compare(r.End, currentKey.Span) <= 0);

            // collect all sources at currentKey
            var sameKey = new List<IScanSource>();
            foreach (var s in sources)
            {
                if (!s.HasItem) continue;
                if (ByteComparer.Compare(s.Key, currentKey) != 0) break;
                sameKey.Add(s);
            }

            // incorporate any new ranges that start at this key
            foreach (var s in sameKey)
                if (s.Kind == DbEntryKind.DeleteRange)
                    activeRanges.Add((s.Key.ToArray(), s.Value.ToArray(), s.Sequence));

            // highest delete-key seq at this key
            ulong deleteKeySeq = 0;
            foreach (var s in sameKey)
                if (s.Kind == DbEntryKind.DeleteKey && s.Sequence > deleteKeySeq)
                    deleteKeySeq = s.Sequence;

            // find latest visible put for this key
            ReadOnlyMemory<byte>? valueToEmit = null;
            foreach (var s in sameKey.Where(x => x.Kind == DbEntryKind.Put)
                         .OrderByDescending(x => x.Sequence).ThenBy(x => x.Precedence))
            {
                if (deleteKeySeq > s.Sequence) continue;
                var covered = activeRanges.Any(r =>
                    ByteComparer.Compare(r.Start, currentKey.Span) <= 0 &&
                    ByteComparer.Compare(currentKey.Span, r.End) < 0 &&
                    r.Seq > s.Sequence);
                if (covered) continue;
                valueToEmit = s.Value;
                break;
            }

            if (valueToEmit.HasValue)
            {
                yield return (currentKey, valueToEmit.Value);
                produced++;
                if (query.Limit.HasValue && produced >= query.Limit.Value) yield break;
            }

            // advance all sources that were at this key
            foreach (var s in sameKey)
                if (!s.MoveNext())
                    sources.Remove(s);
        }
    }

    int _levelsSnapshotCount()
    {
        return _levels.LevelCount;
    }

    async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized) await InitializeAsync(ct);
    }


    async Task<bool> KeyExistsInSstAsync(ReadOnlyMemory<byte> key, CancellationToken ct)
    {
        var snapshot = _levels.SnapshotAll();

        foreach (var f in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            if (!await f.Reader.MightContainAsync(key, ct)) continue;
            var e = await f.Reader.GetAsync(key, ct);
            if (e is { Kind: DbEntryKind.Put }) return true; // treat only PUT as existence
        }

        return false;
    }

    internal async ValueTask CommitTransactionAsync(
        Transaction txn, IReadOnlyList<Mutation> staged,
        CancellationToken ct)
    {
        await _txnManager.CommitTransactionAsync(txn, staged, ct).ConfigureAwait(false);
    }

    async ValueTask CommitMutationsAsync(IList<Mutation> mutations, CancellationToken ct)
    {
        await _txnManager.CommitMutationsAsync(mutations, ct).ConfigureAwait(false);
    }

    async ValueTask<bool> CommitSingleAsync(Mutation m, CancellationToken ct)
    {
        await _commitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var txnId = (ulong)Interlocked.Increment(ref _nextTxnId);
            using var act = TelemetrySources.ActivitySource.StartActivity("Commit.Single");
            act?.SetTag("txn.id", txnId);
            act?.SetTag("op", m.Op.ToString());
            act?.SetTag("key.len", m.Key.Length);

            Log.SingleCommitStarting(_logger, txnId, m.Op, m.Key.Length);
            var existed = false;
            var seq = NextSeq(ref _seq);

            // Validate and prepare entry to write
            DbEntry entry;
            switch (m.Op)
            {
                case MutationOp.Insert:
                    if (_memTableManager.GetMemTable().TryGet(m.Key.Span, out _, out _, out _))
                        throw new GravelInvalidOperationException("Insert failed: key exists (memtable)");
                    if (await KeyExistsInSstAsync(m.Key, ct).ConfigureAwait(false))
                        throw new GravelInvalidOperationException("Insert failed: key exists (sst)");
                    entry = DbEntry.Put(m.Key, m.Value, seq);
                    break;
                case MutationOp.Put:
                    entry = DbEntry.Put(m.Key, m.Value, seq);
                    break;
                case MutationOp.Delete:
                    existed = _memTableManager.GetMemTable().TryGet(m.Key.Span, out _, out _, out var knd) &&
                              knd == DbEntryKind.Put;
                    entry = DbEntry.DeleteKey(m.Key, seq);
                    break;
                case MutationOp.DeleteRange:
                    entry = DbEntry.DeleteRange(m.Key, m.RangeEnd, seq);
                    break;
                default:
                    throw new GravelInvalidOperationException("Unknown mutation op");
            }

            // Use WalManager to perform the transactional WAL write
            await _walManager.WriteTransactionAsync(txnId, [entry], _options.WalSyncOnCommit, ct).ConfigureAwait(false);

            switch (m.Op)
            {
                case MutationOp.Put:
                case MutationOp.Insert: _memTableManager.MemTable.Put(m.Key.Span, m.Value.Span, seq); break;
                case MutationOp.Delete: _memTableManager.MemTable.PutDeleteTombstone(m.Key.Span, seq); break;
                case MutationOp.DeleteRange:
                    _memTableManager.MemTable.PutRangeTombstone(m.Key.Span, m.RangeEnd.Span, seq); break;
                default:
                    throw new GravelInvalidOperationException("Unknown mutation op");
            }

            TelemetrySources.Commits.Add(1);
            Log.SingleCommitCommitted(_logger, txnId, m.Op, seq);
            await MaybeFlushAsync(ct).ConfigureAwait(false);
            return existed;
        }
        finally
        {
            _commitGate.Release();
        }
    }
}
