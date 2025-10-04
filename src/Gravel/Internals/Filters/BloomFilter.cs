using System.IO.Hashing;

namespace Gravel.Internals.Filters;

/// <summary>
///     A compact probabilistic set supporting approximate membership queries.
///     False positives are possible, but false negatives are not.
/// </summary>
public sealed class BloomFilter
{
    readonly byte[] _bits;

    BloomFilter(int bitCount, int hashFunctions)
    {
        Bits = bitCount;
        HashFunctions = hashFunctions;
        _bits = new byte[(Bits + 7) / 8];
    }

    /// <summary>
    ///     Number of bits in the underlying filter.
    /// </summary>
    public int Bits { get; }

    /// <summary>
    ///     Number of hash functions used per insertion/query.
    /// </summary>
    public int HashFunctions { get; }

    /// <summary>
    ///     Creates a Bloom filter sized for the expected number of items and desired false positive rate.
    /// </summary>
    public static BloomFilter Create(int expectedItems, double falsePositiveRate = 0.01)
    {
        if (expectedItems <= 0) expectedItems = 1;
        if (falsePositiveRate <= 0) falsePositiveRate = 0.01;

        // m = -n ln(p) / (ln 2)^2
        var mFloat = -expectedItems * Math.Log(falsePositiveRate) / (Math.Log(2) * Math.Log(2));
        var m = Math.Max(8, (int)Math.Ceiling(mFloat));

        // k = (m/n) ln 2
        var kFloat = m / (double)expectedItems * Math.Log(2);
        var k = Math.Max(1, (int)Math.Round(kFloat));

        return new BloomFilter(m, k);
    }

    /// <summary>
    ///     Inserts an element into the filter.
    /// </summary>
    public void Add(ReadOnlySpan<byte> data)
    {
        var h1 = XxHash64.HashToUInt64(data);
        var seed = unchecked((long)(h1 ^ 0xA5A5A5A5A5A5A5A5UL));
        var h2 = XxHash64.HashToUInt64(data, seed);
        for (var i = 0; i < HashFunctions; i++)
        {
            var combined = (h1 + (ulong)i * h2) % (uint)Bits;
            var idx = (int)combined;
            _bits[idx / 8] |= (byte)(1 << idx % 8);
        }
    }

    /// <summary>
    ///     Checks whether the element may be in the set.
    /// </summary>
    public bool MightContain(ReadOnlySpan<byte> data)
    {
        var h1 = XxHash64.HashToUInt64(data);
        var seed = unchecked((long)(h1 ^ 0xA5A5A5A5A5A5A5A5UL));
        var h2 = XxHash64.HashToUInt64(data, seed);
        for (var i = 0; i < HashFunctions; i++)
        {
            var combined = (h1 + (ulong)i * h2) % (uint)Bits;
            var idx = (int)combined;
            if ((_bits[idx / 8] & 1 << idx % 8) == 0)
                return false;
        }

        return true;
    }

    // expose bits for persistence (e.g. SSTWriter)
    internal ReadOnlyMemory<byte> GetBits()
    {
        return _bits;
    }

    internal static BloomFilter FromBits(int bitCount, int hashFunctions, ReadOnlySpan<byte> bits)
    {
        var bf = new BloomFilter(bitCount, hashFunctions);
        bits.CopyTo(bf._bits);
        return bf;
    }
}
