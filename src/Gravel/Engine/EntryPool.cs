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
        public byte[] Key = null!;
        public int KeyLen;
        public DbEntryKind Kind; // Put, DeleteKey, DeleteRange
        public ulong Seq;
        public byte[] Value = null!; // for DeleteRange this stores range-end; for DeleteKey may be empty
        public int ValueLen;

        public ReadOnlySpan<byte> KeySpan => Key.AsSpan(0, KeyLen);
        public ReadOnlySpan<byte> ValueSpan => Value.AsSpan(0, ValueLen);

        public ReadOnlyMemory<byte> KeyMemory => new(Key, 0, KeyLen);
        public ReadOnlyMemory<byte> ValueMemory => new(Value, 0, ValueLen);
    }
}
