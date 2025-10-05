namespace Gravel.Engine;

/// <summary>
///     Options for controlling backup behavior in the Gravel engine.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>
    ///     If true, enables gzip compression for backup files.
    /// </summary>
    public bool Gzip { get; set; } = true;

    /// <summary>
    ///     Compression level for gzip (0-9). Mapping may be needed for other compressors.
    /// </summary>
    public int CompressionLevel { get; set; } = 6; // 0-9 for gzip (mapping may be needed)

    /// <summary>
    ///     If true, includes WAL (Write-Ahead Log) segments in the backup.
    /// </summary>
    public bool IncludeWalSegments { get; set; } = true;

    /// <summary>
    ///     If true, forces a flush of the MemTable before backup.
    /// </summary>
    public bool ForceMemTableFlush { get; set; } = true;
}
