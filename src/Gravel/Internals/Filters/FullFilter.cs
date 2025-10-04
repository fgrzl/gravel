using System.Buffers.Binary;

namespace Gravel.Internals.Filters;

sealed class FullFilter
{
    readonly BloomFilter _bloom;

    public FullFilter(byte[] raw)
    {
        var bits = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0, 4));
        var k = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4, 4));
        var len = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(8, 4));
        var buf = new byte[len];
        Array.Copy(raw, 12, buf, 0, len);
        _bloom = BloomFilter.FromBits(bits, k, buf);
    }

    public bool MightContain(ReadOnlySpan<byte> key)
    {
        return _bloom.MightContain(key);
    }
}
