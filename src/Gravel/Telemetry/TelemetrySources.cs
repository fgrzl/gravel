using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gravel.Telemetry;

public static class TelemetrySources
{
    // Single ActivitySource used across the project for trace correlation
    public static readonly ActivitySource ActivitySource = new("Gravel", "1.0.0");

    // Single Meter for metrics
    public static readonly Meter Meter = new("Gravel", "1.0.0");

    // Common instruments
    public static readonly Counter<long> Commits = Meter.CreateCounter<long>("gravel.commits");
    public static readonly Counter<long> Flushes = Meter.CreateCounter<long>("gravel.flushes");
    public static readonly Histogram<long> FlushSize = Meter.CreateHistogram<long>("gravel.flush.size");
    public static readonly Counter<long> Compactions = Meter.CreateCounter<long>("gravel.compactions");
    public static readonly Counter<long> WalReplayed = Meter.CreateCounter<long>("gravel.wal.replayed");

    // WAL-specific
    public static readonly Counter<long> WalAppends = Meter.CreateCounter<long>("gravel.wal.appends");
    public static readonly Histogram<long> WalAppendSize = Meter.CreateHistogram<long>("gravel.wal.append.size");

    // SST-specific
    public static readonly Counter<long> SstReads = Meter.CreateCounter<long>("gravel.sst.reads");
    public static readonly Counter<long> SstWrites = Meter.CreateCounter<long>("gravel.sst.writes");
    public static readonly Histogram<long> SstWriteSize = Meter.CreateHistogram<long>("gravel.sst.write.size");
    public static readonly Histogram<long> SstReadSize = Meter.CreateHistogram<long>("gravel.sst.read.size");
}
