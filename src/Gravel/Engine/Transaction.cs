using Gravel.Abstractions;
using Gravel.Exceptions;
using Gravel.Internals;

// ByteComparer

namespace Gravel.Engine;

class Transaction(Engine engine, ulong txnId, ulong beginSequence) : IGravelTransaction
{
    readonly List<Mutation> _staged = [];
    bool _completed; // committed or rolled back

    public ulong TxnId { get; } = txnId;
    public ulong BeginSequence { get; } = beginSequence;
    public ulong? CommitSequence { get; private set; }

    public async ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var got = await GetAsync(key, ct).ConfigureAwait(false);
        return got.HasValue;
    }

    public ValueTask PutAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _staged.Add(Mutation.Put(key, value));
        return ValueTask.CompletedTask;
    }

    public ValueTask InsertAsync(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _staged.Add(Mutation.Insert(key, value));
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _staged.Add(Mutation.Delete(key));
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteRangeAsync(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _staged.Add(Mutation.DeleteRange(start, end));
        return ValueTask.CompletedTask;
    }

    public ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Walk staged ops from newest to oldest and apply last-write-wins semantics
        for (var i = _staged.Count - 1; i >= 0; i--)
        {
            var m = _staged[i];
            // range tombstone covers key
            if (m.Op == MutationOp.DeleteRange)
            {
                if (ByteComparer.Compare(m.Key.Span, key.Span) <= 0 &&
                    ByteComparer.Compare(key.Span, m.RangeEnd.Span) < 0)
                    return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
                continue;
            }

            var cmp = ByteComparer.Compare(m.Key.Span, key.Span);
            if (cmp != 0) continue;

            return m.Op switch
            {
                MutationOp.Put or MutationOp.Insert =>
                    ValueTask.FromResult<ReadOnlyMemory<byte>?>(m.Value),
                MutationOp.Delete => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null),
                _ => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null)
            };
        }

        return engine.GetAsync(key, ct);
    }

    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_completed) throw new GravelException("Transaction already completed");
        await engine.CommitTransactionAsync(this, _staged, ct).ConfigureAwait(false);
        _completed = true;
    }

    public ValueTask RollbackAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_completed) return ValueTask.CompletedTask;
        _staged.Clear();
        _completed = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            // Auto-rollback for safety
            _staged.Clear();
            _completed = true;
        }

        return ValueTask.CompletedTask;
    }

    internal void SetCommitted(ulong sequence)
    {
        CommitSequence = sequence;
    }
}