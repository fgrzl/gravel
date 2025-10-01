using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Compression.Default;

public sealed class DefaultCompressor : IBlockCompressor
{
    public CompressionKind Kind => CompressionKind.None;

    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        return input.ToArray();
    }

    public byte[] Decompress(ReadOnlySpan<byte> input)
    {
        return input.ToArray();
    }

    public int GetMaxCompressedLength(int inputLength)
    {
        return inputLength;
    }

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

    public bool TryGetDecompressedLength(ReadOnlySpan<byte> input, out int uncompressedLength)
    {
        uncompressedLength = input.Length;
        return true;
    }
}