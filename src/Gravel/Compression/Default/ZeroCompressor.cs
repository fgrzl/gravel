using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Compression.Default;

/// <summary>
///     A no-op compressor that performs no compression or decompression.
///     Used for blocks with <see cref="CompressionKind.None" />.
/// </summary>
public sealed class ZeroCompressor : IBlockCompressor
{
    /// <summary>
    ///     Gets the compression kind for this compressor (None).
    /// </summary>
    public CompressionKind Kind => CompressionKind.None;

    /// <summary>
    ///     Returns the input as-is (no compression).
    /// </summary>
    /// <param name="input">The input data.</param>
    /// <returns>The uncompressed input as a byte array.</returns>
    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        return input.ToArray();
    }

    /// <summary>
    ///     Returns the input as-is (no decompression).
    /// </summary>
    /// <param name="input">The input data.</param>
    /// <returns>The decompressed input as a byte array.</returns>
    public byte[] Decompress(ReadOnlySpan<byte> input)
    {
        return input.ToArray();
    }

    /// <summary>
    ///     Returns the maximum compressed length (equal to input length for no-op).
    /// </summary>
    /// <param name="inputLength">The input length.</param>
    /// <returns>The maximum compressed length.</returns>
    public int GetMaxCompressedLength(int inputLength)
    {
        return inputLength;
    }

    /// <summary>
    ///     Copies the input to the destination span if it fits (no compression).
    /// </summary>
    /// <param name="input">The input data.</param>
    /// <param name="destination">The destination span.</param>
    /// <param name="bytesWritten">The number of bytes written.</param>
    /// <returns>True if successful, otherwise false.</returns>
    public bool TryCompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < input.Length)
        {
            bytesWritten = 0;
            return false;
        }

        input.CopyTo(destination);
        bytesWritten = input.Length;
        return true;
    }

    /// <summary>
    ///     Copies the input to the destination span if it fits (no decompression).
    /// </summary>
    /// <param name="input">The input data.</param>
    /// <param name="destination">The destination span.</param>
    /// <param name="bytesWritten">The number of bytes written.</param>
    /// <returns>True if successful, otherwise false.</returns>
    public bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < input.Length)
        {
            bytesWritten = 0;
            return false;
        }

        input.CopyTo(destination);
        bytesWritten = input.Length;
        return true;
    }

    /// <summary>
    ///     Gets the decompressed length (equal to input length for no-op).
    /// </summary>
    /// <param name="input">The input data.</param>
    /// <param name="uncompressedLength">The decompressed length.</param>
    /// <returns>True if successful, otherwise false.</returns>
    public bool TryGetDecompressedLength(ReadOnlySpan<byte> input, out int uncompressedLength)
    {
        uncompressedLength = input.Length;
        return true;
    }
}
