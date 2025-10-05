using Gravel.Internals;

namespace Gravel.Storage.InMemory.Sst;

/// <summary>
///     In-memory SST (Sorted String Table) implementation for storing key-value pairs.
///     Provides fast lookup and ordered enumeration of entries.
/// </summary>
public sealed class InMemorySst
{
    /// <summary>
    ///     Fast lookup dictionary mapping base64-encoded keys to entry index, key, and value.
    /// </summary>
    readonly Dictionary<string, (int Index, byte[] Key, byte[] Value)> _fast;

    /// <summary>
    ///     Ordered array of key-value entries.
    /// </summary>
    public readonly (byte[] Key, byte[] Value)[] Entries;

    /// <summary>
    ///     Initializes a new instance of <see cref="InMemorySst" /> with the specified entries.
    /// </summary>
    /// <param name="entries">The key-value entries to store.</param>
    public InMemorySst(IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries)
    {
        Entries =
        [
            .. entries.Select(e => (e.Key.ToArray(), e.Value.ToArray()))
                .OrderBy(e => e.Item1, ByteArrayComparer.Instance)
        ];
        _fast = new Dictionary<string, (int, byte[], byte[])>(Entries.Length);
        for (var i = 0; i < Entries.Length; i++)
            _fast[Convert.ToBase64String(Entries[i].Key)] = (i, Entries[i].Key, Entries[i].Value);
    }

    /// <summary>
    ///     Tries to get the value for the specified key.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The value if found.</param>
    /// <returns>True if found, otherwise false.</returns>
    public bool TryGet(ReadOnlySpan<byte> key, out byte[]? value)
    {
        if (_fast.TryGetValue(Convert.ToBase64String(key), out var v))
        {
            value = v.Value;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    ///     Comparer for ordering byte arrays lexicographically.
    /// </summary>
    sealed class ByteArrayComparer : IComparer<byte[]>
    {
        /// <summary>
        ///     Singleton instance of the comparer.
        /// </summary>
        public static readonly ByteArrayComparer Instance = new();

        /// <summary>
        ///     Compares two byte arrays lexicographically.
        /// </summary>
        /// <param name="x">The first byte array.</param>
        /// <param name="y">The second byte array.</param>
        /// <returns>An integer indicating the relative order.</returns>
        public int Compare(byte[]? x, byte[]? y)
        {
            return ByteComparer.Compare(x, y);
        }
    }
}
