using Gravel.Abstractions;

namespace Gravel.Engine;

/// <summary>
///     Pool for MemTable entries. Reduces object churn by reusing Entry instances.
///     Buffers (Key/Value arrays) are rented/returned separately from ArrayPool.
/// </summary>
public sealed class EntryPool
{
    readonly Stack<Entry> _pool = new();

    /// <summary>
    ///     Rent an entry object from the pool or allocate a new one.
    ///     Caller is responsible for providing already-rented buffers for Key/Value.
    /// </summary>
    /// <param name="key">The rented key buffer to attach to the entry.</param>
    /// <param name="keyLen">The length of the key in the buffer.</param>
    /// <param name="value">The rented value buffer to attach to the entry.</param>
    /// <param name="valueLen">The length of the value in the buffer.</param>
    /// <param name="seq">The sequence number associated with the entry.</param>
    /// <param name="kind">The kind of database entry.</param>
    /// <returns>A pooled <see cref="Entry"/> instance.</returns>
    public Entry Rent(byte[] key, int keyLen, byte[] value, int valueLen, ulong seq, DbEntryKind kind)
    {
        Entry entry;
        if (_pool.Count == 0)
            entry = new Entry();
        else
            entry = _pool.Pop();

        entry.Key = key;
        entry.KeyLen = keyLen;
        entry.Value = value;
        entry.ValueLen = valueLen;
        entry.Seq = seq;
        entry.Kind = kind;
        return entry;
    }

    /// <summary>
    ///     Return an entry to the pool (does not return Key/Value arrays — those go back to ArrayPool separately).
    /// </summary>
    /// <param name="entry">The entry to return to the pool.</param>
    public void Return(Entry entry)
    {
        entry.Key = null!;
        entry.KeyLen = 0;
        entry.Value = null!;
        entry.ValueLen = 0;
        entry.Seq = 0;
        entry.Kind = 0;
        _pool.Push(entry);
    }

    /// <summary>
    ///     A MemTable entry: wraps key/value buffers and metadata.
    /// </summary>
    public sealed class Entry
    {
        /// <summary>
        ///     The backing buffer that contains the key bytes. Only the first <see cref="KeyLen"/> bytes are valid.
        /// </summary>
        public byte[] Key = null!;

        /// <summary>
        ///     The number of valid bytes in <see cref="Key"/>.
        /// </summary>
        public int KeyLen;

        /// <summary>
        ///     The kind of operation represented by this entry (e.g., Put, DeleteKey, DeleteRange).
        /// </summary>
        public DbEntryKind Kind; // Put, DeleteKey, DeleteRange

        /// <summary>
        ///     The monotonically increasing sequence number assigned to this entry.
        /// </summary>
        public ulong Seq;

        /// <summary>
        ///     The backing buffer that contains the value bytes. For DeleteRange this stores the range end; for DeleteKey it may be empty.
        /// </summary>
        public byte[] Value = null!; // for DeleteRange this stores range-end; for DeleteKey may be empty

        /// <summary>
        ///     The number of valid bytes in <see cref="Value"/>.
        /// </summary>
        public int ValueLen;

        /// <summary>
        ///     A span view over the valid portion of the key buffer.
        /// </summary>
        public ReadOnlySpan<byte> KeySpan => Key.AsSpan(0, KeyLen);

        /// <summary>
        ///     A span view over the valid portion of the value buffer.
        /// </summary>
        public ReadOnlySpan<byte> ValueSpan => Value.AsSpan(0, ValueLen);

        /// <summary>
        ///     A memory view over the valid portion of the key buffer.
        /// </summary>
        public ReadOnlyMemory<byte> KeyMemory => new(Key, 0, KeyLen);

        /// <summary>
        ///     A memory view over the valid portion of the value buffer.
        /// </summary>
        public ReadOnlyMemory<byte> ValueMemory => new(Value, 0, ValueLen);
    }
}
