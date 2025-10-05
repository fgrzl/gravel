using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Sst;

/// <summary>
///     Factory for file-based SST readers and writers.
/// </summary>
/// <param name="options">The SST configuration options.</param>
/// <param name="loggerFactory">Factory for creating component loggers.</param>
/// <param name="compressorFactory">Factory for creating compressor instances.</param>
public sealed class FileSstFactory(
    IOptions<FileSstOptions> options,
    ILoggerFactory loggerFactory,
    ICompressorFactory compressorFactory) : ISstFactory
{
    /// <summary>
    ///     Creates and initializes an SST reader for the given file path.
    /// </summary>
    /// <param name="path">Path to the SST file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An initialized <see cref="ISstReader"/>.</returns>
    public async ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default)
    {
        var reader = (FileSstReader)CreateReader(path);
        await reader.InitializeAsync(ct).ConfigureAwait(false);
        return reader;
    }

    /// <summary>
    ///     Creates and initializes an SST writer for the given file path.
    /// </summary>
    /// <param name="path">Path where the SST will be written.</param>
    /// <param name="expectedEntries">Expected number of entries to help size internal structures.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An initialized <see cref="ISstWriter"/>.</returns>
    public async ValueTask<ISstWriter> CreateWriterAsync(
        string path, int expectedEntries, CancellationToken ct = default)
    {
        var writer = (FileSstWriter)CreateWriter(path, expectedEntries);
        await writer.InitializeAsync(ct).ConfigureAwait(false);
        return writer;
    }

    /// <summary>
    ///     Enumerates SST files in a specific level directory under the given base path.
    /// </summary>
    /// <param name="basePath">The base directory containing level folders.</param>
    /// <param name="level">The level number to enumerate.</param>
    /// <returns>Ordered sequence of <c>.sst</c> file paths.</returns>
    public IEnumerable<string> EnumerateLevelFiles(string basePath, int level)
    {
        var lp = Path.Combine(basePath, $"L{level}");
        if (!Directory.Exists(lp))
            return [];

        return Directory.EnumerateFiles(lp, "*.sst").OrderBy(f => f);
    }

    /// <summary>
    ///     Creates a non-initialized file-based SST reader.
    /// </summary>
    /// <param name="path">Path to the SST file.</param>
    /// <returns>A new <see cref="ISstReader"/>.</returns>
    public ISstReader CreateReader(string path)
    {
        var logger = loggerFactory.CreateLogger<FileSstReader>();
        return new FileSstReader(path, compressorFactory, logger);
    }

    /// <summary>
    ///     Creates a file-based SST writer.
    /// </summary>
    /// <param name="path">Destination file path for the SST.</param>
    /// <param name="expectedEntries">Expected number of entries to guide writer sizing.</param>
    /// <returns>A new <see cref="ISstWriter"/>.</returns>
    public ISstWriter CreateWriter(string path, int expectedEntries)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var bufferSize = options.Value.BufferSize;
        var blockSize = options.Value.BlockSize;
        var compressor = compressorFactory.Get(CompressionKind.Snappy);
        var logger = loggerFactory.CreateLogger<FileSstWriter>();

        return new FileSstWriter(path, expectedEntries, bufferSize, blockSize, compressor, logger);
    }
}

