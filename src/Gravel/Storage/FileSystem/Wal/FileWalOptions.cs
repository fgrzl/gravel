using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Wal;

/// <summary>
/// Options for file-based Write-Ahead Log (WAL).
/// </summary>
public class FileWalOptions : IOptions<FileWalOptions>
{
    /// <summary>
    /// Directory where WAL segments are stored.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Maximum size of a WAL segment in bytes before rolling over to a new file.
    /// </summary>
    public long WalSegmentSize { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Options pattern value accessor.
    /// </summary>
    public FileWalOptions Value => this;
}
