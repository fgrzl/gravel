using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Exceptions;
using Gravel.Internals;
using Gravel.Internals.Compaction;
using Gravel.Logging;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gravel.Engine;

class DbEngine : IDbEngine
{
    readonly SemaphoreSlim _commitGate = new(1, 1);
    readonly ICompactionWorker _compactionWorker;
    readonly SemaphoreSlim _initGate = new(1, 1);
    readonly Levels _levels;
    readonly ILogger<DbEngine> _logger;
    readonly GravelOptions _options;
    readonly string _sstDir;
    readonly ISstFactory _sstFactory;
    readonly string _walDir;
    readonly IWalFactory _walFactory;
    readonly IWalWriter _walWriter;

    bool _disposed;
    bool _initialized;
    MemTable _memTable = new();
    long _nextTxnId;
    ulong _seq; // in-memory sequence allocator base

    public DbEngine(
        IOptions<GravelOptions> options,
        IWalFactory walFactory,
        ISstFactory sstFactory,
        ICompactionWorker compactionWorker,
        ILogger<DbEngine> logger)
    {
        _options = options.Value;

        _walFactory = walFactory;
        _sstFactory = sstFactory;
        _logger = logger;

        // Use configured paths; fall back to sensible defaults under DatabasePath
        _walDir = string.IsNullOrEmpty(_options.WalPath)
            ? Path.Combine(_options.DatabasePath, "wal")
            : _options.WalPath!;
        _walWriter = _walFactory.CreateWriter(_walDir);

        _sstDir = string.IsNullOrEmpty(_options.SstPath)
            ? Path.Combine(_options.DatabasePath, "sst")
            : _options.SstPath!;
        var levelCount = Math.Max(1, _options.SstLevels <= 0 ? 7 : _options.SstLevels);
        _levels = new Levels(levelCount);

        _compactionWorker = compactionWorker;
        Log.EngineCreated(_logger, _options.DatabasePath, _walDir, _sstDir, levelCount);
    }

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
            for (var l = 0;; l++)
            {
                if (l >= _levelsSnapshotCount()) break;
                // ask factory for level files instead of reading filesystem here
                var files = _sstFactory.EnumerateLevelFiles(_sstDir, l);
                foreach (var f in files)
                    try
                    {
                        _levels.Add(l, new SstFile(f, _sstFactory.CreateReader(f), 0));
                        Log.SstLoaded(_logger, l, f);
                    }
                    catch (Exception ex)
                    {
                        Log.SstLoadFailed(_logger, f, ex.Message);
                    }
            }

            await using var rdr = _walFactory.CreateReader(_walDir);
            var staging = new List<DbEntry>();
            var replayed = 0;
            await foreach (var rec in rdr.ReplayAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                switch (rec.Type)
                {
                    case WalConstants.RecordBeginTxn: staging.Clear(); break;
                    case WalConstants.RecordEntry:
                        if (rec.Entry.HasValue)
                        {
                            staging.Add(rec.Entry.Value);
                            replayed++;
                        }

                        break;
                    case WalConstants.RecordCommitTxn:
                        foreach (var e in staging)
                            switch (e.Kind)
                            {
                                case DbEntryKind.Put: _memTable.Put(e.Key.Span, e.Value.Span, e.Sequence); break;
                                case DbEntryKind.DeleteKey: _memTable.PutDeleteTombstone(e.Key.Span, e.Sequence); break;
                                case DbEntryKind.DeleteRange:
                                    _memTable.PutRangeTombstone(e.Key.Span, e.Value.Span, e.Sequence); break;
                            }

                        staging.Clear();
                        break;
                    case WalConstants.RecordRollbackTxn: staging.Clear(); break;
                }
            }

            TelemetrySources.WalReplayed.Add(replayed);
            Log.WalReplayedDetailed(_logger, replayed, _memTable.Count);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        // Delegate to GetAsync so range tombstones and SST lookups are applied consistently
        var got = await GetAsync(key, ct);
        return got.HasValue;
    }

    public async ValueTask PutAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.PutCalled(_logger, key.Length);
        await CommitSingleAsync(Mutation.Put(key, value), ct);
    }

    public async ValueTask InsertAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.InsertCalled(_logger, key.Length);
        await CommitSingleAsync(Mutation.Insert(key, value), ct);
    }

    public async ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // Find latest range tombstone in memtable covering this key using range index (O(log n))
        _memTable.TryGetCoveringRange(key.Span, out var coveringRangeSeq);

        // Check memtable direct entry for this key
        if (_memTable.TryGet(key.Span, out var mtValue, out var mtSeq, out var mtKind))
        {
            if (mtKind == DbEntryKind.Put)
            {
                // Visible only if not masked by a newer range
                if (mtSeq >= coveringRangeSeq) return mtValue;
                return null;
            }

            // DeleteKey or DeleteRange entry at exact key masks it
            return null;
        }

        // If covered by a memtable range tombstone and no newer memtable PUT, mask immediately
        if (coveringRangeSeq > 0)
            return null;

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
                foreach (var (rs, re, rseq) in ranges)
                    if (ByteComparer.Compare(rs.Span, key.Span) <= 0 && ByteComparer.Compare(key.Span, re.Span) < 0)
                        if (!haveCandidate || rseq > bestSeq)
                        {
                            bestSeq = rseq;
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

    public async ValueTask<bool> DeleteAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.DeleteCalled(_logger, key.Length);
        return await CommitSingleAsync(Mutation.Delete(key), ct);
    }

    public async ValueTask DeleteRangeAsync(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        Log.DeleteRangeCalled(_logger, start.Length, end.Length);
        await CommitRangeAsync(start, end, ct);
    }

    public async ValueTask BatchAsync(IEnumerable<Mutation> mutations, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var list = mutations as IList<Mutation> ?? [.. mutations];
        if (list.Count == 0) return;
        Log.BatchCalled(_logger, list.Count);
        await CommitMutationsAsync(list, ct);
    }

    public async IAsyncEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> ScanAsync(Query query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        using var act = TelemetrySources.ActivitySource.StartActivity("Scan");
        act?.SetTag("scan.start", query.Start?.Length ?? -1);
        act?.SetTag("scan.end", query.End?.Length ?? -1);
        act?.SetTag("scan.limit", query.Limit ?? -1);

        Log.ScanCalled(_logger, query.Start?.Length, query.End?.Length, query.Limit);
        var sources = new List<IScanSource> { new MemTableScanSource(_memTable, 0, query.Start, query.End) };
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

    public async ValueTask<IGravelTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var begin = _walWriter.LastSequence;
        var id = (ulong)Interlocked.Increment(ref _nextTxnId);
        Log.BeginTransactionCreated(_logger, id, begin);
        return new Transaction(this, id, begin);
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Disposed(_logger);
        _walWriter.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Log.Disposed(_logger);
        await _walWriter.DisposeAsync();
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


    static IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> MergeSources(List<IScanSource> sources,
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

    internal async ValueTask CommitTransactionAsync(Transaction txn, IReadOnlyList<Mutation> staged,
        CancellationToken ct)
    {
        if (staged.Count == 0) return;
        await _commitGate.WaitAsync(ct);
        try
        {
            using var act = TelemetrySources.ActivitySource.StartActivity("Commit.Transaction");
            act?.SetTag("txn.id", txn.TxnId);
            act?.SetTag("txn.ops", staged.Count);

            Log.TransactionCommitting(_logger, txn.TxnId, staged.Count);
            await _walWriter.BeginTransactionAsync(txn.TxnId, ct);
            var applied = new List<Applied>(staged.Count);
            var seenKeys = new HashSet<string>();
            foreach (var m in staged)
            {
                var seq = NextSeq(ref _seq);
                switch (m.Op)
                {
                    case MutationOp.Insert:
                    {
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        if (seenKeys.Contains(id))
                            throw new GravelInvalidOperationException("Insert failed: key exists (txn)");
                        if (_memTable.TryGet(m.Key.Span, out _, out _, out _))
                            throw new GravelInvalidOperationException("Insert failed: key exists (memtable)");
                        if (await KeyExistsInSstAsync(m.Key, ct))
                            throw new GravelInvalidOperationException("Insert failed: key exists (sst)");
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.Put(m.Key, m.Value, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, m.Value, m.RangeEnd, seq));
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Put:
                    {
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.Put(m.Key, m.Value, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, m.Value, m.RangeEnd, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Delete:
                    {
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.DeleteKey(m.Key, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, default, default, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Remove(id);
                        break;
                    }
                    case MutationOp.DeleteRange:
                    {
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.DeleteRange(m.Key, m.RangeEnd, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, default, m.RangeEnd, seq));
                        break;
                    }
                    default:
                        throw new GravelInvalidOperationException("Unknown mutation op");
                }
            }

            await _walWriter.CommitTransactionAsync(txn.TxnId, ct);
            if (_options.WalSyncOnCommit) await _walWriter.FlushAsync(ct);
            foreach (var a in applied)
                switch (a.Op)
                {
                    case MutationOp.Put:
                    case MutationOp.Insert: _memTable.Put(a.Key.Span, a.Value.Span, a.Sequence); break;
                    case MutationOp.Delete: _memTable.PutDeleteTombstone(a.Key.Span, a.Sequence); break;
                    case MutationOp.DeleteRange:
                        _memTable.PutRangeTombstone(a.Key.Span, a.RangeEnd.Span, a.Sequence); break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

            txn.SetCommitted(_walWriter.LastSequence);
            TelemetrySources.Commits.Add(staged.Count);
            Log.TransactionCommitted(_logger, txn.TxnId, _walWriter.LastSequence);
            await MaybeFlushAsync(ct);
        }
        catch (Exception ex)
        {
            Log.TransactionCommitFailed(_logger, txn.TxnId, ex.Message);
            try
            {
                await _walWriter.RollbackTransactionAsync(txn.TxnId, CancellationToken.None);
            }
            catch (Exception rex)
            {
                Log.RollbackFailed(_logger, txn.TxnId, rex.Message);
            }

            throw;
        }
        finally
        {
            _commitGate.Release();
        }
    }

    async ValueTask CommitMutationsAsync(IList<Mutation> mutations, CancellationToken ct)
    {
        await _commitGate.WaitAsync(ct);
        try
        {
            var txnId = (ulong)Interlocked.Increment(ref _nextTxnId);
            using var act = TelemetrySources.ActivitySource.StartActivity("Commit.Batch");
            act?.SetTag("txn.id", txnId);
            act?.SetTag("txn.ops", mutations.Count);

            Log.TransactionCommitting(_logger, txnId, mutations.Count);
            await _walWriter.BeginTransactionAsync(txnId, ct);
            var applied = new List<Applied>(mutations.Count);
            var seenKeys = new HashSet<string>();
            foreach (var m in mutations)
            {
                var seq = NextSeq(ref _seq);
                switch (m.Op)
                {
                    case MutationOp.Insert:
                    {
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        if (seenKeys.Contains(id))
                            throw new GravelInvalidOperationException("Insert failed: key exists (batch)");
                        if (_memTable.TryGet(m.Key.Span, out _, out _, out _))
                            throw new GravelInvalidOperationException("Insert failed: key exists (memtable)");
                        if (await KeyExistsInSstAsync(m.Key, ct))
                            throw new GravelInvalidOperationException("Insert failed: key exists (sst)");
                        await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, m.Value, m.RangeEnd, seq));
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Put:
                    {
                        await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, m.Value, m.RangeEnd, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Delete:
                    {
                        await _walWriter.AppendAsync(txnId, DbEntry.DeleteKey(m.Key, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, default, default, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Remove(id);
                        break;
                    }
                    case MutationOp.DeleteRange:
                    {
                        await _walWriter.AppendAsync(txnId, DbEntry.DeleteRange(m.Key, m.RangeEnd, seq), ct);
                        applied.Add(new Applied(m.Op, m.Key, default, m.RangeEnd, seq));
                        break;
                    }
                    default: throw new GravelInvalidOperationException("Unknown mutation op");
                }
            }

            await _walWriter.CommitTransactionAsync(txnId, ct);
            if (_options.WalSyncOnCommit) await _walWriter.FlushAsync(ct);
            foreach (var a in applied)
                switch (a.Op)
                {
                    case MutationOp.Put:
                    case MutationOp.Insert: _memTable.Put(a.Key.Span, a.Value.Span, a.Sequence); break;
                    case MutationOp.Delete: _memTable.PutDeleteTombstone(a.Key.Span, a.Sequence); break;
                    case MutationOp.DeleteRange:
                        _memTable.PutRangeTombstone(a.Key.Span, a.RangeEnd.Span, a.Sequence); break;
                }

            TelemetrySources.Commits.Add(mutations.Count);
            Log.MutationsBatchCommitted(_logger, txnId);
            await MaybeFlushAsync(ct);
        }
        finally
        {
            _commitGate.Release();
        }
    }

    async ValueTask CommitRangeAsync(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end, CancellationToken ct)
    {
        await CommitSingleAsync(Mutation.DeleteRange(start, end), ct);
    }


    async ValueTask MaybeFlushAsync(CancellationToken ct)
    {
        if (_memTable.Count < _options.MemTableThreshold) return;
        Log.FlushTrigger(_logger, _memTable.Count, _options.MemTableThreshold);
        await FlushMemTableAsync(ct);
    }

    async ValueTask FlushMemTableAsync(CancellationToken ct)
    {
        var seqTag = _walWriter.LastSequence;
        var dir = Path.Combine(_sstDir, "L0");
        // Directory.CreateDirectory(dir); // removed: let factory handle directory creation
        var path = Path.Combine(dir, $"{seqTag:D20}.sst");
        var mt = _memTable;

        using var act = TelemetryHelper.StartActivityScope(TelemetrySources.ActivitySource, _logger,
            "Flush.MemTable");
        act?.SetTag("sst.path", path);
        act?.SetTag("entries", mt.Count);

        Log.FlushEnqueued(_logger, mt.Count);
        await using (var w = _sstFactory.CreateWriter(path, mt.Count))
        {
            await w.WriteAsync(EnumerateMemTableEntriesAsync(mt, ct), ct);
        }

        _memTable = new MemTable();
        _levels.Add(0, new SstFile(path, _sstFactory.CreateReader(path), seqTag));

        TelemetrySources.Flushes.Add(1);
        TelemetrySources.FlushSize.Record(mt.Count);
        Log.SstCreated(_logger, path);
        Log.FlushComplete(_logger, path, seqTag);

        await MaybeCompactAsync(ct);
    }

    static async IAsyncEnumerable<DbEntry> EnumerateMemTableEntriesAsync(MemTable mt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var (k, v, seq, kind) in mt.Scan())
        {
            ct.ThrowIfCancellationRequested();
            var e = kind switch
            {
                DbEntryKind.Put => DbEntry.Put(k, v, seq),
                DbEntryKind.DeleteKey => DbEntry.DeleteKey(k, seq),
                DbEntryKind.DeleteRange => DbEntry.DeleteRange(k, v, seq),
                _ => default
            };
            yield return e;
            await Task.Yield();
        }
    }

    async ValueTask MaybeCompactAsync(CancellationToken ct)
    {
        for (var l = 0; l < _levels.LevelCount - 1; l++)
        {
            if (!_levels.MeetsFanIn(l, _options.CompactionFanInThreshold)) continue;
            var snapshot = _levels.SnapshotLevels();
            var to = snapshot[l].ToList(); // don't remove yet; keep visible until success
            if (to.Count == 0) continue;
            var next = l + 1;
            var outDir = Path.Combine(_sstDir, $"L{next}");
            // Directory.CreateDirectory(outDir); // removed: let factory handle directory creation
            var outPath = Path.Combine(outDir, $"{DateTime.UtcNow.Ticks:D20}.sst");

            using var act = TelemetrySources.ActivitySource.StartActivity("Compaction.Pass");
            act?.SetTag("from_level", l);
            act?.SetTag("file_count", to.Count);
            act?.SetTag("to_level", next);

            Log.CompactionStarted(_logger, l, to.Count, next);

            // Enqueue compaction task instead of doing inline merge
            var task = new MergeFilesCompactionTask(to, outPath, _sstFactory,
                Compactor.EstimateMergedCount(to), (p, inputs) =>
                {
                    // callback invoked on task success: install new SST and remove inputs atomically
                    var newR = _sstFactory.CreateReader(p);
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

            // break after scheduling one pass to let background worker run
            break;
        }
    }

    async ValueTask<bool> CommitSingleAsync(Mutation m, CancellationToken ct)
    {
        await _commitGate.WaitAsync(ct);
        try
        {
            var txnId = (ulong)Interlocked.Increment(ref _nextTxnId);
            using var act = TelemetrySources.ActivitySource.StartActivity("Commit.Single");
            act?.SetTag("txn.id", txnId);
            act?.SetTag("op", m.Op.ToString());
            act?.SetTag("key.len", m.Key.Length);

            Log.SingleCommitStarting(_logger, txnId, m.Op, m.Key.Length);
            await _walWriter.BeginTransactionAsync(txnId, ct);
            var existed = false;
            var seq = NextSeq(ref _seq);
            switch (m.Op)
            {
                case MutationOp.Insert:
                    if (_memTable.TryGet(m.Key.Span, out _, out _, out _))
                        throw new GravelInvalidOperationException("Insert failed: key exists (memtable)");
                    if (await KeyExistsInSstAsync(m.Key, ct))
                        throw new GravelInvalidOperationException("Insert failed: key exists (sst)");
                    await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct);
                    break;
                case MutationOp.Put:
                    await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct);
                    break;
                case MutationOp.Delete:
                    existed = _memTable.TryGet(m.Key.Span, out _, out _, out var knd) && knd == DbEntryKind.Put;
                    await _walWriter.AppendAsync(txnId, DbEntry.DeleteKey(m.Key, seq), ct);
                    break;
                case MutationOp.DeleteRange:
                    await _walWriter.AppendAsync(txnId, DbEntry.DeleteRange(m.Key, m.RangeEnd, seq), ct);
                    break;
                default:
                    throw new GravelInvalidOperationException("Unknown mutation op");
            }

            await _walWriter.CommitTransactionAsync(txnId, ct);
            if (_options.WalSyncOnCommit) await _walWriter.FlushAsync(ct);
            switch (m.Op)
            {
                case MutationOp.Put:
                case MutationOp.Insert: _memTable.Put(m.Key.Span, m.Value.Span, seq); break;
                case MutationOp.Delete: _memTable.PutDeleteTombstone(m.Key.Span, seq); break;
                case MutationOp.DeleteRange: _memTable.PutRangeTombstone(m.Key.Span, m.RangeEnd.Span, seq); break;
                default:
                    throw new GravelInvalidOperationException("Unknown mutation op");
            }

            TelemetrySources.Commits.Add(1);
            Log.SingleCommitCommitted(_logger, txnId, m.Op, seq);
            await MaybeFlushAsync(ct);
            return existed;
        }
        finally
        {
            _commitGate.Release();
        }
    }
}