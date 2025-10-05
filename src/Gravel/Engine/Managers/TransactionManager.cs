using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Exceptions;
using Gravel.Logging;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;

namespace Gravel.Engine.Managers;

internal sealed class TransactionManager(
    MemTableManager memTableManager,
    Levels levels,
    IWalWriter walWriter,
    GravelOptions options,
    SemaphoreSlim commitGate,
    ILogger logger,
    Func<ulong> allocateSeq,
    Func<ulong> allocateTxnId,
    Func<CancellationToken, ValueTask> maybeFlushAsync)
{
    readonly MemTableManager _memTableManager = memTableManager ?? throw new ArgumentNullException(nameof(memTableManager));
    readonly Levels _levels = levels ?? throw new ArgumentNullException(nameof(levels));
    readonly IWalWriter _walWriter = walWriter ?? throw new ArgumentNullException(nameof(walWriter));
    readonly GravelOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    readonly SemaphoreSlim _commitGate = commitGate ?? throw new ArgumentNullException(nameof(commitGate));
    readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    readonly Func<ulong> _allocateSeq = allocateSeq ?? throw new ArgumentNullException(nameof(allocateSeq));
    readonly Func<ulong> _allocateTxnId = allocateTxnId ?? throw new ArgumentNullException(nameof(allocateTxnId));
    readonly Func<CancellationToken, ValueTask> _maybeFlushAsync = maybeFlushAsync ?? throw new ArgumentNullException(nameof(maybeFlushAsync));

    public async ValueTask CommitTransactionAsync(Transaction txn, IReadOnlyList<Mutation> staged, CancellationToken ct)
    {
        if (staged.Count == 0) return;
        await _commitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var act = TelemetrySources.ActivitySource.StartActivity("Commit.Transaction");
            act?.SetTag("txn.id", txn.TxnId);
            act?.SetTag("txn.ops", staged.Count);

            Log.TransactionCommitting(_logger, txn.TxnId, staged.Count);
            await _walWriter.BeginTransactionAsync(txn.TxnId, ct).ConfigureAwait(false);
            var applied = new List<DbEntry>(staged.Count);
            var seenKeys = new HashSet<string>();
            foreach (var m in staged)
            {
                var seq = _allocateSeq();
                switch (m.Op)
                {
                    case MutationOp.Insert:
                    {
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        if (seenKeys.Contains(id))
                            throw new GravelInvalidOperationException("Insert failed: key exists (txn)");
                        if (_memTableManager.ContainsKey(m.Key))
                            throw new GravelInvalidOperationException("Insert failed: key exists (memtable)");
                        if (await KeyExistsInSstAsync(m.Key, ct).ConfigureAwait(false))
                            throw new GravelInvalidOperationException("Insert failed: key exists (sst)");
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.Put(m.Key, m.Value, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.Put(m.Key, m.Value, seq));
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Put:
                    {
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.Put(m.Key, m.Value, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.Put(m.Key, m.Value, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Delete:
                    {
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.DeleteKey(m.Key, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.DeleteKey(m.Key, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Remove(id);
                        break;
                    }
                    case MutationOp.DeleteRange:
                    {
                        await _walWriter.AppendAsync(txn.TxnId, DbEntry.DeleteRange(m.Key, m.RangeEnd, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.DeleteRange(m.Key, m.RangeEnd, seq));
                        break;
                    }
                    default:
                        throw new GravelInvalidOperationException("Unknown mutation op");
                }
            }

            await _walWriter.CommitTransactionAsync(txn.TxnId, ct).ConfigureAwait(false);
            if (_options.WalSyncOnCommit) await _walWriter.FlushAsync(ct).ConfigureAwait(false);
            // Apply staged entries to memtable via manager
            _memTableManager.ApplyStagedEntries(applied);

            txn.SetCommitted(_walWriter.LastSequence);
            TelemetrySources.Commits.Add(staged.Count);
            Log.TransactionCommitted(_logger, txn.TxnId, _walWriter.LastSequence);
            await _maybeFlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.TransactionCommitFailed(_logger, txn.TxnId, ex.Message);
            try
            {
                await _walWriter.RollbackTransactionAsync(txn.TxnId, CancellationToken.None).ConfigureAwait(false);
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

    public async ValueTask CommitMutationsAsync(
        IList<Mutation> mutations,
        CancellationToken ct)
    {
        await _commitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var txnId = _allocateTxnId();
            using var act = TelemetrySources.ActivitySource.StartActivity("Commit.Batch");
            act?.SetTag("txn.id", txnId);
            act?.SetTag("txn.ops", mutations.Count);

            Log.TransactionCommitting(_logger, txnId, mutations.Count);
            await _walWriter.BeginTransactionAsync(txnId, ct).ConfigureAwait(false);
            var applied = new List<DbEntry>(mutations.Count);
            var seenKeys = new HashSet<string>();
            foreach (var m in mutations)
            {
                var seq = _allocateSeq();
                switch (m.Op)
                {
                    case MutationOp.Insert:
                    {
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        if (seenKeys.Contains(id))
                            throw new GravelInvalidOperationException("Insert failed: key exists (batch)");
                        if (_memTableManager.ContainsKey(m.Key))
                            throw new GravelInvalidOperationException("Insert failed: key exists (memtable)");
                        if (await KeyExistsInSstAsync(m.Key, ct).ConfigureAwait(false))
                            throw new GravelInvalidOperationException("Insert failed: key exists (sst)");
                        await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.Put(m.Key, m.Value, seq));
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Put:
                    {
                        await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.Put(m.Key, m.Value, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Add(id);
                        break;
                    }
                    case MutationOp.Delete:
                    {
                        await _walWriter.AppendAsync(txnId, DbEntry.DeleteKey(m.Key, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.DeleteKey(m.Key, seq));
                        var id = Convert.ToBase64String(m.Key.ToArray());
                        seenKeys.Remove(id);
                        break;
                    }
                    case MutationOp.DeleteRange:
                    {
                        await _walWriter.AppendAsync(txnId, DbEntry.DeleteRange(m.Key, m.RangeEnd, seq), ct).ConfigureAwait(false);
                        applied.Add(DbEntry.DeleteRange(m.Key, m.RangeEnd, seq));
                        break;
                    }
                    default: throw new GravelInvalidOperationException("Unknown mutation op");
                }
            }

            await _walWriter.CommitTransactionAsync(txnId, ct).ConfigureAwait(false);
            if (_options.WalSyncOnCommit) await _walWriter.FlushAsync(ct).ConfigureAwait(false);
            _memTableManager.ApplyStagedEntries(applied);

            TelemetrySources.Commits.Add(mutations.Count);
            Log.MutationsBatchCommitted(_logger, txnId);
            await _maybeFlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _commitGate.Release();
        }
    }

    public async ValueTask CommitRangeAsync(
        ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end,
        CancellationToken ct)
    {
        await CommitSingleAsync(Mutation.DeleteRange(start, end), ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> CommitSingleAsync(
        Mutation m, CancellationToken ct)
    {
        await _commitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var txnId = _allocateTxnId();
            using var act = TelemetrySources.ActivitySource.StartActivity("Commit.Single");
            act?.SetTag("txn.id", txnId);
            act?.SetTag("op", m.Op.ToString());
            act?.SetTag("key.len", m.Key.Length);

                Log.SingleCommitStarting(_logger, txnId, m.Op, m.Key.Length);
                await _walWriter.BeginTransactionAsync(txnId, ct).ConfigureAwait(false);
                var existed = false;
                var seq = _allocateSeq();
                switch (m.Op)
                {
                    case MutationOp.Insert:
                        if (_memTableManager.ContainsKey(m.Key))
                            throw new GravelInvalidOperationException("Insert failed: key exists (memtable)");
                        if (await KeyExistsInSstAsync(m.Key, ct).ConfigureAwait(false))
                            throw new GravelInvalidOperationException("Insert failed: key exists (sst)");
                        await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct).ConfigureAwait(false);
                        break;
                    case MutationOp.Put:
                        await _walWriter.AppendAsync(txnId, DbEntry.Put(m.Key, m.Value, seq), ct).ConfigureAwait(false);
                        break;
                    case MutationOp.Delete:
                        existed = _memTableManager.IsPut(m.Key);
                        await _walWriter.AppendAsync(txnId, DbEntry.DeleteKey(m.Key, seq), ct).ConfigureAwait(false);
                        break;
                    case MutationOp.DeleteRange:
                        await _walWriter.AppendAsync(txnId, DbEntry.DeleteRange(m.Key, m.RangeEnd, seq), ct).ConfigureAwait(false);
                        break;
                    default:
                        throw new GravelInvalidOperationException("Unknown mutation op");
                }

                await _walWriter.CommitTransactionAsync(txnId, ct).ConfigureAwait(false);
                if (_options.WalSyncOnCommit) await _walWriter.FlushAsync(ct).ConfigureAwait(false);
                // apply single staged entry
                var single = m.Op switch
                {
                    MutationOp.Put or MutationOp.Insert => DbEntry.Put(m.Key, m.Value, seq),
                    MutationOp.Delete => DbEntry.DeleteKey(m.Key, seq),
                    MutationOp.DeleteRange => DbEntry.DeleteRange(m.Key, m.RangeEnd, seq),
                    _ => default
                };
                _memTableManager.ApplyStagedEntries(new List<DbEntry> { single });

                TelemetrySources.Commits.Add(1);
                Log.SingleCommitCommitted(_logger, txnId, m.Op, seq);
                await _maybeFlushAsync(CancellationToken.None).ConfigureAwait(false);
                return existed;
        }
        finally
        {
            _commitGate.Release();
        }
    }

    async Task<bool> KeyExistsInSstAsync(ReadOnlyMemory<byte> key, CancellationToken ct)
    {
        var snapshot = _levels.SnapshotAll();

        foreach (var f in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            if (!await f.Reader.MightContainAsync(key, ct).ConfigureAwait(false)) continue;
            var e = await f.Reader.GetAsync(key, ct).ConfigureAwait(false);
            if (e is { Kind: DbEntryKind.Put }) return true; // treat only PUT as existence
        }

        return false;
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
}
