using System.Buffers.Binary;
using Gravel.Internals.Filters;

namespace Gravel.Storage.Shared;

/// <summary>
///     Builds a full Bloom filter block for an SST file.
/// </summary>
/// <param name="expectedEntries">Expected number of entries to size the filter.</param>
public sealed class FullFilterBlockBuilder(int expectedEntries)
{
    readonly BloomFilter _bloom = BloomFilter.Create(expectedEntries);

    /// <summary>
    ///     Adds a key to the Bloom filter.
    /// </summary>
    /// <param name="key">The user key bytes.</param>
    public void AddKey(ReadOnlySpan<byte> key)
    {
        _bloom.Add(key);
    }

    /// <summary>
    ///     Finalizes the filter and returns its serialized representation.
    /// </summary>
    /// <returns>Serialized filter bytes: bits, hash function count, and bit array length followed by data.</returns>
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
