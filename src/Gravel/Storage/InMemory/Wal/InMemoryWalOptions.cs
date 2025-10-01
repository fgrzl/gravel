namespace Gravel.Storage.InMemory.Wal;

/// <summary>
///     Options controlling the in-memory WAL implementation.
/// </summary>
public sealed class InMemoryWalOptions
{
    /// <summary>
    ///     If true a single shared writer instance is reused for all requested directories.
    ///     This mirrors file WAL behaviour without creating isolated state per logical path.
    /// </summary>
    public bool SharedWriter { get; set; } = true;

    /// <summary>
    ///     Maximum number of WAL records retained in memory before oldest entries are dropped.
    ///     (Purely for test/integration usage to avoid unbounded growth.)
    /// </summary>
    public int MaxBufferedRecords { get; set; } = 1_000_000;
}