using System;
using System.Text;
using Gravel.Abstractions;
using Gravel.Internals.Compaction;
using Gravel.Storage.InMemory.Sst;
using Gravel.Storage.InMemory.Wal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gravel.Engine;

public class EngineGetPerformanceTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static DbEngine CreateEngine(
        InMemorySstFactory sstFactory,
        InMemoryWalFactory walFactory,
        GravelOptions? opts = null)
    {
        var options = Options.Create(opts ?? new GravelOptions
        {
            DatabasePath = "mem://perf",
            MemTableThreshold = 10_000,
            SstLevels = 2,
            WalSyncOnCommit = true
        });

        var worker = new CompactionWorker(double.MaxValue);
        return new DbEngine(options, walFactory, sstFactory, worker, NullLogger<DbEngine>.Instance);
    }
}