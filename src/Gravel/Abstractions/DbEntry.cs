namespace Gravel.Abstractions;

/// <summary>
///     Represents a database operation with an associated sequence number.
/// </summary>
public readonly record struct DbEntry(
    DbEntryKind Kind,
    ReadOnlyMemory<byte> Key,
    ReadOnlyMemory<byte> Value,
    ulong Sequence)
{
    /// <summary>
    ///     The type of entry (Put, DeleteKey, DeleteRange).
    /// </summary>
    public DbEntryKind Kind { get; } = Kind;

    /// <summary>
    ///     The primary key or range-start for this entry.
    /// </summary>
    public ReadOnlyMemory<byte> Key { get; } = Key;

    /// <summary>
    ///     For Put: the value;
    ///     For DeleteRange: the range-end key (exclusive);
    ///     For DeleteKey: empty.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; } = Value;

    /// <summary>
    ///     The sequence number for versioning.
    /// </summary>
    public ulong Sequence { get; } = Sequence;

    /// <summary>
    ///     Convenience factory for Put.
    /// </summary>
    /// <param name="key">The key to put.</param>
    /// <param name="value">The value to associate.</param>
    /// <param name="seq">The sequence number.</param>
    /// <returns>A new <see cref="DbEntry"/> instance.</returns>
    public static DbEntry Put(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, ulong seq)
    {
        return new DbEntry(DbEntryKind.Put, key, value, seq);
    }

    /// <summary>
    ///     Convenience factory for single-key delete.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="seq">The sequence number.</param>
    /// <returns>A new <see cref="DbEntry"/> instance.</returns>
    public static DbEntry DeleteKey(ReadOnlyMemory<byte> key, ulong seq)
    {
        return new DbEntry(DbEntryKind.DeleteKey, key, ReadOnlyMemory<byte>.Empty, seq);
    }

    /// <summary>
    ///     Convenience factory for range delete.
    /// </summary>
    /// <param name="start">The start key (inclusive).</param>
    /// <param name="end">The end key (exclusive).</param>
    /// <param name="seq">The sequence number.</param>
    /// <returns>A new <see cref="DbEntry"/> instance.</returns>
    public static DbEntry DeleteRange(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end, ulong seq)
    {
        return new DbEntry(DbEntryKind.DeleteRange, start, end, seq);
    }
}
