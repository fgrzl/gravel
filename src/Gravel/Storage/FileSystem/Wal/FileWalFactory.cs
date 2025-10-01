using Gravel.Abstractions.Storage.Wal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Wal;

public class FileWalFactory(IOptionsMonitor<FileWalOptions> options, ILoggerFactory loggerFactory)
    : IWalFactory
{
    public IWalReader CreateReader(string directory)
    {
        var path = !string.IsNullOrEmpty(directory) ? directory : options.CurrentValue.Path ?? directory;
        if (!string.IsNullOrEmpty(path)) Directory.CreateDirectory(path); // factory ensures existence
        var logger = loggerFactory.CreateLogger<FileWalReader>();
        return new FileWalReader(path, logger);
    }

    public IWalWriter CreateWriter(string directory)
    {
        var path = !string.IsNullOrEmpty(directory) ? directory : options.CurrentValue.Path ?? directory;
        if (!string.IsNullOrEmpty(path)) Directory.CreateDirectory(path); // factory ensures existence
        var segSize = options.CurrentValue.WalSegmentSize;
        var logger = loggerFactory.CreateLogger<FileWalWriter>();
        return new FileWalWriter(path, segSize, logger);
    }
}