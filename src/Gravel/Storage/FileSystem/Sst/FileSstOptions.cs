using Gravel.Abstractions.Storage.Sst;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Sst;

/// <summary>
///     Options for file-based SST storage.
/// </summary>
public sealed record FileSstOptions : SstOptions, IOptions<FileSstOptions>
{
    /// <summary>
    ///     Initializes a new instance of <see cref="FileSstOptions" /> with sensible defaults.
    /// </summary>
    public FileSstOptions()
    {
        // Provide sensible defaults for required base options
        Kind = "file";
        SparseInterval = 128;
    }

    /// <summary>
    ///     Base directory for SST files.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    ///     Target uncompressed data block size in bytes.
    /// </summary>
    public int BlockSize { get; init; } = 64 * 1024;

    /// <summary>
    ///     I/O buffer size for file operations in bytes.
    /// </summary>
    public int BufferSize { get; init; } = 64 * 1024;

    /// <summary>
    ///     Use memory-mapped I/O for reading.
    /// </summary>
    public bool UseMemoryMapped { get; init; } = true;

    /// <summary>
    ///     Options pattern value accessor.
    /// </summary>
    public FileSstOptions Value => this;
}
