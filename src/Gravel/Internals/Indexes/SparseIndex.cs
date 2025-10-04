using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace Gravel.Internals.Indexes;

public sealed class SparseIndex
{
    // flattened key storage: all key bytes stored in a single blob for better locality
    byte[] _blob = [];
    int _blobLen;
    ulong[] _hashes = [];
    int[] _keyLens = [];

    int[] _keyStarts = [];
    long[] _offsets = [];

    public int Count { get; private set; }

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

    void EnsureBlobCapacity(int required)
    {
        if (_blob.Length >= required) return;
        var newSize = Math.Max(required, _blob.Length == 0 ? 256 : _blob.Length * 2);
        var newBlob = new byte[newSize];
        if (_blobLen > 0) Array.Copy(_blob, 0, newBlob, 0, _blobLen);
        _blob = newBlob;
    }

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
