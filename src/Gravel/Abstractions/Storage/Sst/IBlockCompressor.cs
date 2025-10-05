namespace Gravel.Abstractions.Storage.Sst;

/// <summary>
///     Interface for block compression algorithms used in SST files.
///     Provides legacy and allocation-free APIs for compressing and decompressing data blocks.
/// </summary>
public interface IBlockCompressor
{
    /// <summary>
    ///     Gets the compression kind implemented by this compressor.
    /// </summary>
    CompressionKind Kind { get; }

    /// <summary>
    ///     Compresses the input data and returns the compressed result as a byte array.
    /// </summary>
    /// <param name="input">The input data to compress.</param>
    /// <returns>The compressed data as a byte array.</returns>
    byte[] Compress(ReadOnlySpan<byte> input);

    /// <summary>
    ///     Decompresses the input data and returns the decompressed result as a byte array.
    /// </summary>
    /// <param name="input">The input data to decompress.</param>
    /// <returns>The decompressed data as a byte array.</returns>
    byte[] Decompress(ReadOnlySpan<byte> input);

    /// <summary>
    ///     Returns the maximum possible compressed length for a given input length.
    ///     Used to size destination buffers for allocation-free compression.
    /// </summary>
    /// <param name="inputLength">The input length in bytes.</param>
    /// <returns>The maximum compressed length in bytes.</returns>
    int GetMaxCompressedLength(int inputLength)
    {
        // Conservative upper bound used by many Snappy/LZ4 impls
        return inputLength + (inputLength >> 3) + 40;
    }

    /// <summary>
    ///     Attempts to compress the input data into the provided destination span.
    ///     Returns true if successful, false if the destination is too small.
    /// </summary>
    /// <param name="input">The input data to compress.</param>
    /// <param name="destination">The destination span for compressed data.</param>
    /// <param name="bytesWritten">The number of bytes written to the destination.</param>
    /// <returns>True if compression succeeded, otherwise false.</returns>
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

    /// <summary>
    ///     Attempts to decompress the input data into the provided destination span.
    ///     Returns true if successful, false if the destination is too small.
    /// </summary>
    /// <param name="input">The input data to decompress.</param>
    /// <param name="destination">The destination span for decompressed data.</param>
    /// <param name="bytesWritten">The number of bytes written to the destination.</param>
    /// <returns>True if decompression succeeded, otherwise false.</returns>
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

    /// <summary>
    ///     Attempts to get the decompressed length of the input data.
    ///     Returns true if the length is known, otherwise false.
    /// </summary>
    /// <param name="input">The input data.</param>
    /// <param name="uncompressedLength">The decompressed length, if known.</param>
    /// <returns>True if the length is known, otherwise false.</returns>
    bool TryGetDecompressedLength(ReadOnlySpan<byte> input, out int uncompressedLength)
    {
        uncompressedLength = 0;
        return false; // unknown by default; implementations may override
    }
}
