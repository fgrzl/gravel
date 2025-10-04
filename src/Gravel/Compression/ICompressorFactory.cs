using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Compression;

public interface ICompressorFactory
{
    IBlockCompressor Get(CompressionKind kind);
}
