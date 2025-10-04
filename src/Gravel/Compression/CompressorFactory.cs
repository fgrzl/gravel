using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression.Default;
using Gravel.Compression.Snappy;

namespace Gravel.Compression;

public sealed class CompressorFactory : ICompressorFactory
{
    readonly IBlockCompressor _none = new DefaultCompressor();
    readonly IBlockCompressor _snappy = new SnappyCompressor();

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
