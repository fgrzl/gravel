namespace Gravel.Abstractions.Storage.Sst;

public interface IBlockCompressor
{
    CompressionKind Kind { get; }

    // Legacy convenience API (may allocate)
    byte[] Compress(ReadOnlySpan<byte> input);
    byte[] Decompress(ReadOnlySpan<byte> input);

    // Allocation-free fast paths (default implementations fall back to legacy methods)
    int GetMaxCompressedLength(int inputLength)
    {
        // Conservative upper bound used by many Snappy/LZ4 impls
        return inputLength + (inputLength >> 3) + 40;
    }

    bool TryCompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        var tmp = Compress(input);
        if (tmp.Length > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        tmp.AsSpan().CopyTo(destination);
        bytesWritten = tmp.Length;
        return true;
    }

    bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        var tmp = Decompress(input);
        if (tmp.Length > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        tmp.AsSpan().CopyTo(destination);
        bytesWritten = tmp.Length;
        return true;
    }

    bool TryGetDecompressedLength(ReadOnlySpan<byte> input, out int uncompressedLength)
    {
        uncompressedLength = 0;
        return false; // unknown by default; implementations may override
    }
}
