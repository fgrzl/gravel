using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Sst;

public sealed class FileSstFactory(
    IOptionsMonitor<FileSstOptions> options,
    ILoggerFactory loggerFactory,
    ICompressorFactory compressorFactory) : ISstFactory
{
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

        var bufferSize = options.CurrentValue.BufferSize;
        var blockSize = options.CurrentValue.BlockSize;
        var compressor = compressorFactory.Get(CompressionKind.Snappy);
        var logger = loggerFactory.CreateLogger<FileSstWriter>();

        return new FileSstWriter(path, expectedEntries, bufferSize, blockSize, compressor, logger);
    }

    public IEnumerable<string> EnumerateLevelFiles(string basePath, int level)
    {
        var lp = Path.Combine(basePath, $"L{level}");
        if (!Directory.Exists(lp))
            return [];

        return Directory.EnumerateFiles(lp, "*.sst").OrderBy(f => f);
    }
}