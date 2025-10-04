namespace Gravel.Engine;

public sealed class BackupOptions
{
    public bool Gzip { get; set; } = true;
    public int CompressionLevel { get; set; } = 6; // 0-9 for gzip (mapping may be needed)
    public bool IncludeWalSegments { get; set; } = true;
    public bool ForceMemTableFlush { get; set; } = true;
}
