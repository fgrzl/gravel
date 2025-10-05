using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression.Default;
using Gravel.Compression.Snappy;

namespace Gravel.Compression;

/// <summary>
///     Factory for creating block compressors based on the specified compression kind.
/// </summary>
public sealed class CompressorFactory : ICompressorFactory
{
    readonly IBlockCompressor _none = new ZeroCompressor();
    readonly IBlockCompressor _snappy = new SnappyCompressor();

    /// <summary>
    ///     Gets an <see cref="IBlockCompressor" /> instance for the specified <see cref="CompressionKind" />.
    /// </summary>
    /// <param name="kind">The compression kind to use.</param>
    /// <returns>An <see cref="IBlockCompressor" /> for the requested kind.</returns>
    /// <exception cref="NotSupportedException">Thrown if the compression kind is not supported.</exception>
    public IBlockCompressor Get(CompressionKind kind)
    {
        return kind switch
        {
            CompressionKind.Snappy => _snappy,
            CompressionKind.None => _none,
            _ => throw new NotSupportedException($"Compression kind {kind} not supported")
        };
    }
}
