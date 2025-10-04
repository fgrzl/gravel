using System;
using System.Text;
using Gravel.Abstractions;
using Gravel.Internals.Compaction;
using Gravel.Storage.InMemory.Sst;
using Gravel.Storage.InMemory.Wal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gravel.Engine;

public class TransactionEdgeTests
{
    // Convenience overload that uses a no-op compaction worker for tests.
    static IDbEngine CreateEngine(
        InMemorySstFactory sstFactory,
        InMemoryWalFactory walFactory,
        GravelOptions? opts = null)
    {
        return CreateEngine(sstFactory, walFactory, new NoopCompactionWorker(), opts);
    }

    static IDbEngine CreateEngine(
        InMemorySstFactory sstFactory,
        InMemoryWalFactory walFactory,
        ICompactionWorker worker,
        GravelOptions? opts = null)
    {
        var options = Options.Create(opts ?? new GravelOptions
        {
            DatabasePath = "mem://txdb",
            MemTableThreshold = 100,
            SstLevels = 2,
            WalSyncOnCommit = true
        });
        return new DbEngine(options, walFactory, sstFactory, worker, NullLogger<DbEngine>.Instance);
    }

    static InMemorySstFactory SstFactory()
    {
        return new InMemorySstFactory(Options.Create(new InMemorySstOptions()));
    }

    static InMemoryWalFactory WalFactory()
    {
        return new InMemoryWalFactory(Options.Create(new InMemoryWalOptions()));
    }

    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }
}