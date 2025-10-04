namespace Gravel.Abstractions;

/// <summary>
///     Represents a single operation against the key/value store.
///     Used in both transactions and batched execution.
/// </summary>
public readonly struct Mutation
{
    public MutationOp Op { get; }
    public ReadOnlyMemory<byte> Key { get; } // used for Put/Delete
    public ReadOnlyMemory<byte> Value { get; } // only valid for Put/Insert
    public TimeSpan? Ttl { get; } // only valid for Put/Insert
    public ReadOnlyMemory<byte> RangeEnd { get; } // only valid for DeleteRange

    Mutation(
        MutationOp op,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> value,
        TimeSpan? ttl,
        ReadOnlyMemory<byte> rangeEnd)
    {
        Op = op;
        Key = key;
        Value = value;
        Ttl = ttl;
        RangeEnd = rangeEnd;
    }

    public static Mutation Put(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null)
    {
        return new Mutation(MutationOp.Put, key, value, ttl, ReadOnlyMemory<byte>.Empty);
    }

    public static Mutation Insert(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null)
    {
        return new Mutation(MutationOp.Insert, key, value, ttl, ReadOnlyMemory<byte>.Empty);
    }

    public static Mutation Delete(ReadOnlyMemory<byte> key)
    {
        return new Mutation(MutationOp.Delete, key, ReadOnlyMemory<byte>.Empty, null, ReadOnlyMemory<byte>.Empty);
    }

    public static Mutation DeleteRange(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end)
    {
        return new Mutation(MutationOp.DeleteRange, start, ReadOnlyMemory<byte>.Empty, null, end);
    }
}
