namespace Gravel.Abstractions;

/// <summary>
///     Represents a single operation against the key/value store.
///     Used in both transactions and batched execution.
/// </summary>
public readonly struct Mutation
{
    /// <summary>
    ///     The operation type (Put, Insert, Delete, DeleteRange).
    /// </summary>
    public MutationOp Op { get; }

    /// <summary>
    ///     The key for Put/Delete operations.
    /// </summary>
    public ReadOnlyMemory<byte> Key { get; }

    /// <summary>
    ///     The value for Put/Insert operations.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <summary>
    ///     Optional time-to-live for Put/Insert operations.
    /// </summary>
    public TimeSpan? Ttl { get; }

    /// <summary>
    ///     The end key for DeleteRange operations.
    /// </summary>
    public ReadOnlyMemory<byte> RangeEnd { get; }

    /// <summary>
    ///     Creates a new <see cref="Mutation" /> instance.
    /// </summary>
    /// <param name="op">The operation type.</param>
    /// <param name="key">The key for the operation.</param>
    /// <param name="value">The value for Put/Insert.</param>  
    /// <param name="ttl">Optional time-to-live.</param>
    /// <param name="rangeEnd">The end key for DeleteRange.</param>
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

    /// <summary>
    ///     Creates a Put mutation.
    /// </summary>
    /// <param name="key">The key for the mutation.</param>
    /// <param name="value">The value for the mutation.</param>
    /// <param name="ttl">The optional time-to-live for the mutation.</param>
    /// <returns>A new Mutation instance configured as a Put operation.</returns>
    public static Mutation Put(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null)
    {
        return new Mutation(MutationOp.Put, key, value, ttl, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    ///     Creates an Insert mutation.
    /// </summary>
    /// <param name="key">The key for the mutation.</param>
    /// <param name="value">The value for the mutation.</param>
    /// <param name="ttl">The optional time-to-live for the mutation.</param>
    /// <returns>A new Mutation instance configured as an Insert operation.</returns>
    public static Mutation Insert(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, TimeSpan? ttl = null)
    {
        return new Mutation(MutationOp.Insert, key, value, ttl, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    ///     Creates a Delete mutation.
    /// </summary>
    /// <param name="key">The key for the mutation.</param>
    /// <returns>A new Mutation instance configured as a Delete operation.</returns>
    public static Mutation Delete(ReadOnlyMemory<byte> key)
    {
        return new Mutation(MutationOp.Delete, key, ReadOnlyMemory<byte>.Empty, null, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    ///     Creates a DeleteRange mutation.
    /// </summary>
    /// <param name="start">The start key for the range.</param>
    /// <param name="end">The end key for the range.</param>
    /// <returns>A new Mutation instance configured as a DeleteRange operation.</returns>
    public static Mutation DeleteRange(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end)
    {
        return new Mutation(MutationOp.DeleteRange, start, ReadOnlyMemory<byte>.Empty, null, end);
    }
}
