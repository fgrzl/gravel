using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Gravel.Internals.Indexes;

/// <summary>
///     Sparse index for mapping keys to offsets, using flattened key storage for locality and fast lookup.
/// </summary>
public sealed class SparseIndex
{
    /// <summary>
    ///     All key bytes stored in a single blob for better locality.
    /// </summary>
    byte[] _blob = [];

    int _blobLen;

    /// <summary>
    ///     Hashes of each key for fast lookup.
    /// </summary>
    ulong[] _hashes = [];

    /// <summary>
    ///     Lengths of each key.
    /// </summary>
    int[] _keyLens = [];

    /// <summary>
    ///     Start positions of each key in the blob.
    /// </summary>
    int[] _keyStarts = [];

    /// <summary>
    ///     Offsets associated with each key.
    /// </summary>
    long[] _offsets = [];

    /// <summary>
    ///     Gets the number of entries in the index.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    ///     Enumerates all key/offset pairs in the index.
    /// </summary>
    public IEnumerable<(byte[] Key, long Offset)> Entries
    {
        get
        {
            for (var i = 0; i < Count; i++)
            {
                var len = _keyLens[i];
                var start = _keyStarts[i];
                var key = new byte[len];
                if (len > 0) Array.Copy(_blob, start, key, 0, len);
                yield return (key, _offsets[i]);
            }
        }
    }

    /// <summary>
    ///     Adds a key/offset sample to the index. Keys must be added in ascending order.
    /// </summary>
    /// <param name="key">The key to add.</param>
    /// <param name="offset">The offset associated with the key.</param>
    public void AddSample(byte[] key, long offset)
    {
        if (Count > 0)
        {
            var prevStart = _keyStarts[Count - 1];
            var prevLen = _keyLens[Count - 1];
            if (ByteComparer.Compare(key, _blob.AsSpan(prevStart, prevLen)) < 0)
                throw new InvalidOperationException("Keys must be added in ascending order.");
        }

        if (Count == _keyStarts.Length)
            GrowArrays();

        EnsureBlobCapacity(_blobLen + key.Length);
        if (key.Length > 0)
            Array.Copy(key, 0, _blob, _blobLen, key.Length);

        _keyStarts[Count] = _blobLen;
        _keyLens[Count] = key.Length;
        _offsets[Count] = offset;
        _hashes[Count] = XxHash64.HashToUInt64(key);
        _blobLen += key.Length;
        Count++;
    }

    /// <summary>
    ///     Grows internal arrays to accommodate more entries.
    /// </summary>
    void GrowArrays()
    {
        var newSize = _keyStarts.Length == 0 ? 4 : _keyStarts.Length * 2;
        var newStarts = new int[newSize];
        var newLens = new int[newSize];
        var newOffsets = new long[newSize];
        var newHashes = new ulong[newSize];
        if (Count > 0)
        {
            Array.Copy(_keyStarts, 0, newStarts, 0, Count);
            Array.Copy(_keyLens, 0, newLens, 0, Count);
            Array.Copy(_offsets, 0, newOffsets, 0, Count);
            Array.Copy(_hashes, 0, newHashes, 0, Count);
        }

        _keyStarts = newStarts;
        _keyLens = newLens;
        _offsets = newOffsets;
        _hashes = newHashes;
    }

    /// <summary>
    ///     Ensures the key blob has enough capacity for new keys.
    /// </summary>
    /// <param name="required">The required capacity.</param>
    void EnsureBlobCapacity(int required)
    {
        if (_blob.Length >= required) return;
        var newSize = Math.Max(required, _blob.Length == 0 ? 256 : _blob.Length * 2);
        var newBlob = new byte[newSize];
        if (_blobLen > 0) Array.Copy(_blob, 0, newBlob, 0, _blobLen);
        _blob = newBlob;
    }

    /// <summary>
    ///     Finds the offset of the largest key less than or equal to the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>The offset of the floor key, or 0 if not found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long FindFloor(ReadOnlySpan<byte> key)
    {
        if (Count == 0) return 0L;

        var starts = _keyStarts;
        var lens = _keyLens;
        var offs = _offsets;

        int lo = 0, hi = Count - 1, pos = -1;
        while (lo <= hi)
        {
            var mid = lo + hi >> 1;
            var cmp = ByteComparer.Compare(_blob.AsSpan(starts[mid], lens[mid]), key);

            if (cmp <= 0)
            {
                pos = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return pos >= 0 ? offs[pos] : 0L;
    }
}
