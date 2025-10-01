using Microsoft.Extensions.Options;

namespace Gravel.Storage.FileSystem.Wal;

public class FileWalOptions : IOptions<FileWalOptions>
{
    public required string Path { get; set; }

    public long WalSegmentSize { get; set; } = 64 * 1024 * 1024;

    public FileWalOptions Value => this;
}