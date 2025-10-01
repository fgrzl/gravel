using System.Buffers.Binary;
using Gravel.Internals.Filters;

namespace Gravel.Abstractions.Storage.Sst;

sealed class FullFilterBlockBuilder
{
    readonly BloomFilter _bloom;

    public FullFilterBlockBuilder(int expectedEntries)
    {
        _bloom = BloomFilter.Create(expectedEntries);
    }

    public void AddKey(ReadOnlySpan<byte> key)
    {
        _bloom.Add(key);
    }

    public byte[] Finish()
    {
        var bitsMem = _bloom.GetBits();
        var bits = bitsMem.ToArray();
        using var ms = new MemoryStream();
        Span<byte> h = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(h[..4], _bloom.Bits);
        BinaryPrimitives.WriteInt32LittleEndian(h.Slice(4, 4), _bloom.HashFunctions);
        BinaryPrimitives.WriteInt32LittleEndian(h.Slice(8, 4), bits.Length);
        ms.Write(h);
        ms.Write(bits);
        return ms.ToArray();
    }
}