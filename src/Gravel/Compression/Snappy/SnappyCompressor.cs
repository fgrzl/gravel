using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Compression.Snappy;

/// <summary>
///     Implements the Snappy block compressor for use with Gravel storage.
/// </summary>
public sealed class SnappyCompressor : IBlockCompressor
{
    /// <summary>
    ///     Gets the compression kind (Snappy).
    /// </summary>
    public CompressionKind Kind => CompressionKind.Snappy;

    /// <summary>
    ///     Compresses the input data using the Snappy algorithm.
    /// </summary>
    /// <param name="input">The input data to compress.</param>
    /// <returns>A compressed byte array containing the Snappy-encoded data.</returns>
    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        return SnappyCodec.Compress(input);
    }

    /// <summary>
    ///     Decompresses Snappy-compressed data into a new byte array.
    /// </summary>
    /// <param name="input">The compressed input data.</param>
    /// <returns>The decompressed byte array.</returns>
    public byte[] Decompress(ReadOnlySpan<byte> input)
    {
        return SnappyCodec.Decompress(input);
    }

    /// <summary>
    ///     Gets the maximum possible compressed length for a given input size.
    /// </summary>
    /// <param name="inputLength">The length of the input data.</param>
    /// <returns>The maximum compressed length.</returns>
    public int GetMaxCompressedLength(int inputLength)
    {
        // Matches Snappy helper estimation (n + n/8 + 32) with safety slop
        return inputLength + (inputLength >> 3) + 40;
    }

    /// <summary>
    ///     Compresses the input data into a caller-provided buffer using the Snappy algorithm.
    /// </summary>
    /// <param name="input">The input data to compress.</param>
    /// <param name="destination">The buffer to receive the compressed data.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
    /// <returns>True if compression succeeded and the buffer was large enough; otherwise, false.</returns>
    public bool TryCompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        return SnappyCodec.TryCompress(input, destination, out bytesWritten);
    }

    /// <summary>
    ///     Decompresses Snappy-compressed data into a caller-provided buffer.
    /// </summary>
    /// <param name="input">The compressed input data.</param>
    /// <param name="destination">The buffer to receive the decompressed data.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
    /// <returns>True if decompression succeeded and the buffer was large enough; otherwise, false.</returns>
    public bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        return SnappyCodec.TryDecompress(input, destination, out bytesWritten);
    }

    /// <summary>
    ///     Attempts to read the uncompressed length from a Snappy-compressed buffer.
    /// </summary>
    /// <param name="input">The compressed input data.</param>
    /// <param name="uncompressedLength">The decoded uncompressed length.</param>
    /// <returns>True if the length was successfully read; otherwise, false.</returns>
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
