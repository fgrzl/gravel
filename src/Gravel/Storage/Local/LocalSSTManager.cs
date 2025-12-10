using Gravel.Cloud.SST;
using Microsoft.Extensions.Logging;

namespace Gravel.Storage.Local;

/// <summary>
///     Local-only SST storage. All files stored on disk.
///     Uses cloud-native TLV format for consistency.
/// </summary>
public sealed class LocalSSTManager : IAsyncDisposable
{
    readonly string _sstDir;
    readonly ILogger _logger;
    readonly Dictionary<string, LocalSSTFile> _files = new();

    /// <summary>
    ///     Initializes a new instance of <see cref="LocalSSTManager" />.
    /// </summary>
    public LocalSSTManager(string sstDir, ILogger? logger = null)
    {
        _sstDir = sstDir ?? throw new ArgumentNullException(nameof(sstDir));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        Directory.CreateDirectory(_sstDir);
        LoadExistingFiles();
    }

    /// <summary>
    ///     Writes an SST file to disk.
    /// </summary>
    public async ValueTask<string> WriteAsync(
        IAsyncEnumerable<(byte Type, System.ReadOnlyMemory<byte> Key, System.ReadOnlyMemory<byte> Value, ulong Sequence)> entries,
        string? fileName = null,
        System.Threading.CancellationToken ct = default)
    {
        fileName ??= $"sst-{DateTime.UtcNow.Ticks:D20}.sst";
        var path = Path.Combine(_sstDir, fileName);

        // Create directory if needed
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var fileStream = File.Create(path))
        {
            await using var writer = new CloudNativeSSTWriter(fileStream, sparseIndexInterval: 64);
            await writer.WriteEntriesAsync(entries, ct).ConfigureAwait(false);
        }

        var fileInfo = new FileInfo(path);
        var sstFile = new LocalSSTFile
        {
            Path = path,
            FileName = fileName,
            SizeBytes = fileInfo.Length,
            CreatedAtUtc = fileInfo.CreationTimeUtc
        };

        _files[fileName] = sstFile;
        _logger.LogDebug("SST file written: {FileName}, {Size} bytes", fileName, fileInfo.Length);

        return path;
    }

    /// <summary>
    ///     Reads an SST file.
    /// </summary>
    public async ValueTask<CloudNativeSSTReader> ReadAsync(string fileName, System.Threading.CancellationToken ct = default)
    {
        if (!_files.TryGetValue(fileName, out var sstFile))
        {
            throw new FileNotFoundException($"SST file not found: {fileName}");
        }

        var data = await File.ReadAllBytesAsync(sstFile.Path, ct).ConfigureAwait(false);
        return new CloudNativeSSTReader(data);
    }

    /// <summary>
    ///     Lists all SST files in a level.
    /// </summary>
    public IEnumerable<string> ListLevel(int level)
    {
        var levelDir = Path.Combine(_sstDir, $"L{level}");
        if (!Directory.Exists(levelDir))
            return Enumerable.Empty<string>();

        return Directory.GetFiles(levelDir, "*.sst")
            .Select(Path.GetFileName)
            .Where(f => f != null)!;
    }

    /// <summary>
    ///     Deletes an SST file.
    /// </summary>
    public void Delete(string fileName)
    {
        if (_files.TryGetValue(fileName, out var sstFile))
        {
            try
            {
                File.Delete(sstFile.Path);
                _files.Remove(fileName);
                _logger.LogDebug("SST file deleted: {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete SST file: {FileName}", fileName);
            }
        }
    }

    /// <summary>
    ///     Gets statistics about stored files.
    /// </summary>
    public LocalSSTStats GetStats()
    {
        var totalSize = _files.Values.Sum(f => f.SizeBytes);
        return new LocalSSTStats
        {
            FileCount = _files.Count,
            TotalSizeBytes = totalSize,
            Files = _files.Values.ToList()
        };
    }

    private void LoadExistingFiles()
    {
        try
        {
            foreach (var level in Directory.EnumerateDirectories(_sstDir))
            {
                foreach (var file in Directory.GetFiles(level, "*.sst"))
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileName(file);

                    _files[fileName] = new LocalSSTFile
                    {
                        Path = file,
                        FileName = fileName,
                        SizeBytes = fileInfo.Length,
                        CreatedAtUtc = fileInfo.CreationTimeUtc
                    };

                    _logger.LogDebug("Loaded existing SST file: {FileName}", fileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading existing SST files");
        }
    }

    /// <summary>
    ///     Disposes the manager.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}

/// <summary>
///     Local SST file metadata.
/// </summary>
public sealed class LocalSSTFile
{
    /// <summary>
    ///     Full file system path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    ///     File name only.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    ///     Size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    ///     UTC timestamp when file was created.
    /// </summary>
    public required DateTime CreatedAtUtc { get; init; }
}

/// <summary>
///     Statistics about local SST storage.
/// </summary>
public sealed class LocalSSTStats
{
    /// <summary>
    ///     Number of files.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    ///     Total size in bytes.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    ///     List of files.
    /// </summary>
    public List<LocalSSTFile> Files { get; init; } = [];
}
