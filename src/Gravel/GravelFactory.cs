using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Compression;
using Gravel.Engine;
using Gravel.Internals.Compaction;
using Gravel.Storage.FileSystem.Sst;
using Gravel.Storage.FileSystem.Wal;
using Gravel.Storage.InMemory.Sst;
using Gravel.Storage.InMemory.Wal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gravel;

public static class GravelFactory
{
    public static async Task<IDbEngine> CreateInMemoryAsync(CancellationToken ct = default)
    {
        var sst = new InMemorySstFactory(Options.Create(new InMemorySstOptions()));
        var wal = new InMemoryWalFactory(Options.Create(new InMemoryWalOptions()));
        var engine = CreateEngine(sst, wal, new GravelOptions
        {
            DatabasePath = "mem://factory",
            MemTableThreshold = 1000,
            SstLevels = 7,
            WalSyncOnCommit = true
        });
        await engine.InitializeAsync(ct);
        return engine;
    }

    public static async Task<IDbEngine> CreateFileSystemAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        var sstOptions = new FileSstOptions { Path = path, Kind = "file", SparseInterval = 128 };
        var sst = new FileSstFactory(sstOptions, NullLoggerFactory.Instance, new CompressorFactory());

        var walOptions = new FileWalOptions { Path = path, WalSegmentSize = 64 * 1024 };
        var wal = new FileWalFactory(walOptions, NullLoggerFactory.Instance);

        var engine = CreateEngine(sst, wal, new GravelOptions
        {
            DatabasePath = path,
            MemTableThreshold = 1000,
            SstLevels = 7,
            WalSyncOnCommit = true
        });
        await engine.InitializeAsync(ct);
        return engine;
    }

    static IDbEngine CreateEngine(ISstFactory sstFactory, IWalFactory walFactory, GravelOptions options)
    {
        var worker = new CompactionWorker(double.MaxValue);
        return new DbEngine(Options.Create(options), walFactory, sstFactory, worker, NullLogger<DbEngine>.Instance);
    }
}