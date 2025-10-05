using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Compression;

/// <summary>
///     Factory interface for obtaining block compressors based on compression kind.
/// </summary>
public interface ICompressorFactory
{
    /// <summary>
    ///     Gets an <see cref="IBlockCompressor" /> instance for the specified <see cref="CompressionKind" />.
    /// </summary>
    /// <param name="kind">The compression kind to use.</param>
    /// <returns>An <see cref="IBlockCompressor" /> for the requested kind.</returns>
    IBlockCompressor Get(CompressionKind kind);
}
