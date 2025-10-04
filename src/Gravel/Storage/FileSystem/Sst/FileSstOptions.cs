using Gravel.Abstractions.Storage.Sst;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Sst;

public sealed record FileSstOptions : SstOptions, IOptions<FileSstOptions>
{
    public FileSstOptions()
    {
        // Provide sensible defaults for required base options
        Kind = "file";
        SparseInterval = 128;
    }

    public required string Path { get; set; }
    public int BlockSize { get; init; } = 64 * 1024;
    public int BufferSize { get; init; } = 64 * 1024;
    public bool UseMemoryMapped { get; init; } = true;
    public FileSstOptions Value => this;
}
