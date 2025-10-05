using Gravel.Abstractions.Storage.Wal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Wal;

/// <summary>
/// Factory for file-based WAL readers and writers.
/// </summary>
/// <param name="options">WAL configuration options.</param>
/// <param name="loggerFactory">Factory used to create component loggers.</param>
public class FileWalFactory(IOptions<FileWalOptions> options, ILoggerFactory loggerFactory)
    : IWalFactory
{
    /// <summary>
    /// Creates a WAL reader for the specified directory.
    /// Ensures the directory exists.
    /// </summary>
    /// <param name="directory">Directory containing WAL segments.</param>
    /// <returns>An <see cref="IWalReader"/> instance.</returns>
    public IWalReader CreateReader(string directory)
    {
        var path = !string.IsNullOrEmpty(directory) ? directory : options.Value.Path ?? directory;
        if (!string.IsNullOrEmpty(path))
            Directory.CreateDirectory(path); // factory ensures existence

        var logger = loggerFactory.CreateLogger<FileWalReader>();
        return new FileWalReader(path, logger);
    }

    /// <summary>
    /// Creates a WAL writer for the specified directory.
    /// Ensures the directory exists and applies configured segment size.
    /// </summary>
    /// <param name="directory">Target WAL directory.</param>
    /// <returns>An <see cref="IWalWriter"/> instance.</returns>
    public IWalWriter CreateWriter(string directory)
    {
        var path = !string.IsNullOrEmpty(directory) ? directory : options.Value.Path ?? directory;
        if (!string.IsNullOrEmpty(path))
            Directory.CreateDirectory(path); // factory ensures existence

        var segSize = options.Value.WalSegmentSize;
        var logger = loggerFactory.CreateLogger<FileWalWriter>();
        return new FileWalWriter(path, segSize, logger);
    }
}
