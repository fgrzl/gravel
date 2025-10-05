using Gravel.Engine;
using Microsoft.Extensions.Options;

namespace Gravel.Abstractions;

/// <summary>
///     Provides configuration options for a <see cref="DbEngine" /> instance and its components
///     (WAL, SST files, and compaction).
/// </summary>
public sealed class GravelOptions : IOptions<GravelOptions>
{
    /// <summary>
    ///     The base filesystem path for the database or, when used with <see cref="SstFile" />, the full
    ///     path to the SST file being read or written.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    ///     Optional override path for WAL files. If not set, WAL files will be placed under <see cref="DatabasePath" />/"wal".
    /// </summary>
    public string? WalPath { get; set; }

    /// <summary>
    ///     Optional override path for SST files. If not set, SST files will be placed under <see cref="DatabasePath" />/"sst".
    /// </summary>
    public string? SstPath { get; set; }

    /// <summary>
    ///     Maximum number of entries in the memtable before triggering a flush to an SST file.
    /// </summary>
    public int MemTableThreshold { get; set; } = 1024;

    /// <summary>
    ///     Number of SST levels to retain in the LSM hierarchy.
    /// </summary>
    public int SstLevels { get; set; } = 7;

    /// <summary>
    ///     When true, opens the database in read-only mode where possible. Some operations will be disabled.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    ///     If true, durability is ensured by flushing the WAL during transaction commit (group commit).
    ///     If false, the WAL uses write-through semantics to reduce the need for explicit fsync on commit.
    /// </summary>
    public bool WalSyncOnCommit { get; set; } = true;

    /// <summary>
    ///     Number of files in a level required to trigger a compaction (fan-in threshold).
    /// </summary>
    public int CompactionFanInThreshold { get; set; } = 4;

    /// <summary>
    ///     Maximum number of concurrent compaction tasks to run.
    /// </summary>
    public int MaxConcurrentCompactions { get; set; } = 2;

    /// <summary>
    ///     Gets the current <see cref="GravelOptions"/> instance (IOptions implementation).
    /// </summary>
    public GravelOptions Value => this;
}
