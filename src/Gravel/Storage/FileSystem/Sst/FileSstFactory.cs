using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Sst;

public sealed class FileSstFactory(
    IOptions<FileSstOptions> options,
    ILoggerFactory loggerFactory,
    ICompressorFactory compressorFactory) : ISstFactory
{
    public async ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default)
    {
        var reader = (FileSstReader)CreateReader(path);
        await reader.InitializeAsync(ct).ConfigureAwait(false);
        return reader;
    }

    public async ValueTask<ISstWriter> CreateWriterAsync(
        string path, int expectedEntries, CancellationToken ct = default)
    {
        var writer = (FileSstWriter)CreateWriter(path, expectedEntries);
        await writer.InitializeAsync(ct).ConfigureAwait(false);
        return writer;
    }

    public IEnumerable<string> EnumerateLevelFiles(string basePath, int level)
    {
        var lp = Path.Combine(basePath, $"L{level}");
        if (!Directory.Exists(lp))
            return [];

        return Directory.EnumerateFiles(lp, "*.sst").OrderBy(f => f);
    }

    public ISstReader CreateReader(string path)
    {
        var logger = loggerFactory.CreateLogger<FileSstReader>();
        return new FileSstReader(path, compressorFactory, logger);
    }

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
