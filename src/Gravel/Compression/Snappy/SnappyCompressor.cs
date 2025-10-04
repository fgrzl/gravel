using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Compression.Snappy;

public sealed class SnappyCompressor : IBlockCompressor
{
    public CompressionKind Kind => CompressionKind.Snappy;

    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        return SnappyCodec.Compress(input);
    }

    public byte[] Decompress(ReadOnlySpan<byte> input)
    {
        return SnappyCodec.Decompress(input);
    }

    public int GetMaxCompressedLength(int inputLength)
    {
        // Matches Snappy helper estimation (n + n/8 + 32) with safety slop
        return inputLength + (inputLength >> 3) + 40;
    }

    public bool TryCompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        // Use convenience then copy for now; could be optimized to write directly if helper exposes span-based API
        var tmp = SnappyCodec.Compress(input);
        if (tmp.Length > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        tmp.AsSpan().CopyTo(destination);
        bytesWritten = tmp.Length;
        return true;
    }

    public bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        // Peek uncompressed length from Snappy framing to avoid extra alloc when possible
        if (TryGetDecompressedLength(input, out var len) && len <= destination.Length)
        {
            var tmp = SnappyCodec.Decompress(input);
            tmp.AsSpan().CopyTo(destination);
            bytesWritten = tmp.Length;
            return true;
        }

        // Fallback
        var outArr = SnappyCodec.Decompress(input);
        if (outArr.Length > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        outArr.AsSpan().CopyTo(destination);
        bytesWritten = outArr.Length;
        return true;
    }

    public bool TryGetDecompressedLength(ReadOnlySpan<byte> input, out int uncompressedLength)
    {
        // The Snappy helper supports TryReadVarint at position 0 internally; expose a lightweight probe
        // Re-parse varint similarly to SnappyCodec.Decompress
        var pos = 0;
        uint result = 0;
        var shift = 0;
        while (pos < input.Length)
        {
            var b = input[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                uncompressedLength = (int)result;
                return true;
            }

            shift += 7;
            if (shift > 35) break;
        }

        uncompressedLength = 0;
        return false;
    }
}
