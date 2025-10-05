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
        return SnappyCodec.TryCompress(input, destination, out bytesWritten);
    }

    public bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        return SnappyCodec.TryDecompress(input, destination, out bytesWritten);
    }

    public bool TryGetDecompressedLength(ReadOnlySpan<byte> input, out int uncompressedLength)
    {
        // The Snappy helper supports TryReadVarInt at position 0 internally; expose a lightweight probe
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
