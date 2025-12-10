using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gravel.Telemetry;

/// <summary>
///     Provides common telemetry sources and instruments for tracing and metrics in Gravel.
/// </summary>
public static class TelemetrySources
{
    /// <summary>
    ///     Single <see cref="ActivitySource" /> used across the project for trace correlation.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Gravel", "1.0.0");

    /// <summary>
    ///     Single <see cref="Meter" /> for metrics.
    /// </summary>
    public static readonly Meter Meter = new("Gravel", "1.0.0");

    // ============ DbEngine Metrics ============

    /// <summary>
    ///     Counter for PUT operations.
    /// </summary>
    public static readonly Counter<long> DbPuts = Meter.CreateCounter<long>("gravel.db.puts");

    /// <summary>
    ///     Counter for GET operations.
    /// </summary>
    public static readonly Counter<long> DbGets = Meter.CreateCounter<long>("gravel.db.gets");

    /// <summary>
    ///     Counter for DELETE operations.
    /// </summary>
    public static readonly Counter<long> DbDeletes = Meter.CreateCounter<long>("gravel.db.deletes");

    /// <summary>
    ///     Counter for range delete operations.
    /// </summary>
    public static readonly Counter<long> DbRangeDeletes = Meter.CreateCounter<long>("gravel.db.range.deletes");

    /// <summary>
    ///     Counter for batch operations.
    /// </summary>
    public static readonly Counter<long> DbBatches = Meter.CreateCounter<long>("gravel.db.batches");

    /// <summary>
    ///     Counter for memtable flushes.
    /// </summary>
    public static readonly Counter<long> DbFlushes = Meter.CreateCounter<long>("gravel.db.flushes");

    /// <summary>
    ///     Counter for WAL recovery operations.
    /// </summary>
    public static readonly Counter<long> DbRecoveries = Meter.CreateCounter<long>("gravel.db.recoveries");

    // ============ WAL Metrics ============

    /// <summary>
    ///     Counter for the number of commits.
    /// </summary>
    public static readonly Counter<long> Commits = Meter.CreateCounter<long>("gravel.commits");

    /// <summary>
    ///     Counter for the number of memtable flushes.
    /// </summary>
    public static readonly Counter<long> Flushes = Meter.CreateCounter<long>("gravel.flushes");

    /// <summary>
    ///     Histogram for the size of memtable flushes.
    /// </summary>
    public static readonly Histogram<long> FlushSize = Meter.CreateHistogram<long>("gravel.flush.size");

    /// <summary>
    ///     Counter for the number of compactions.
    /// </summary>
    public static readonly Counter<long> Compactions = Meter.CreateCounter<long>("gravel.compactions");

    /// <summary>
    ///     Counter for the number of WAL segments replayed.
    /// </summary>
    public static readonly Counter<long> WalReplayed = Meter.CreateCounter<long>("gravel.wal.replayed");

    /// <summary>
    ///     Counter for the number of WAL appends.
    /// </summary>
    public static readonly Counter<long> WalAppends = Meter.CreateCounter<long>("gravel.wal.appends");

    /// <summary>
    ///     Histogram for the size of WAL appends.
    /// </summary>
    public static readonly Histogram<long> WalAppendSize = Meter.CreateHistogram<long>("gravel.wal.append.size");

    // ============ SST Metrics ============

    /// <summary>
    ///     Counter for the number of SST reads.
    /// </summary>
    public static readonly Counter<long> SstReads = Meter.CreateCounter<long>("gravel.sst.reads");

    /// <summary>
    ///     Counter for the number of SST writes.
    /// </summary>
    public static readonly Counter<long> SstWrites = Meter.CreateCounter<long>("gravel.sst.writes");

    /// <summary>
    ///     Histogram for the size of SST writes.
    /// </summary>
    public static readonly Histogram<long> SstWriteSize = Meter.CreateHistogram<long>("gravel.sst.write.size");

    /// <summary>
    ///     Histogram for the size of SST reads.
    /// </summary>
    public static readonly Histogram<long> SstReadSize = Meter.CreateHistogram<long>("gravel.sst.read.size");
}
