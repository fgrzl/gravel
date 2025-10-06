using System;
using System.Text;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Internals.Compaction;
using Gravel.Storage.InMemory.Wal;
using Gravel.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Engine;

public class EngineMixedTombstoneTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static DbEngine CreateEngineWithFactory(TestSstFactory factory)
    {
        var opts = Options.Create(new GravelOptions
        {
            DatabasePath = "/y",
            MemTableThreshold = 100,
            SstLevels = 3,
            WalSyncOnCommit = true
        });
        var walFactory = new InMemoryWalFactory(Options.Create(new InMemoryWalOptions()))
            as IWalFactory;
        var worker = new CompactionWorker(double.MaxValue);
        return new DbEngine(opts, walFactory, factory, worker, NullLogger<DbEngine>.Instance);
    }

    [Fact]
    public async Task should_apply_delete_key_over_put_on_tie_and_range_over_older_put_across_levels()
    {
        // Arrange
        var factory = new TestSstFactory();
        // L2 newer delete-key at same seq as put (tie -> delete-key wins), and newer range masking older put
        factory.Register("/y/sst", 2, "0002.sst", new[]
        {
            DbEntry.DeleteKey(B("b"), 50UL),
            DbEntry.DeleteRange(B("x"), B("z"), 60UL)
        });
        factory.Register("/y/sst", 1, "0001.sst", new[]
        {
            DbEntry.Put(B("b"), B("vb"), 50UL) // same seq as delete-key
        });
        factory.Register("/y/sst", 0, "0000.sst", new[]
        {
            DbEntry.Put(B("y"), B("vy"), 10UL)
        });

        var eng = CreateEngineWithFactory(factory);
        await eng.InitializeAsync();

        // Act
        var gb = await eng.GetAsync(B("b"));
        var gy = await eng.GetAsync(B("y"));

        // Assert
        Assert.Null(gb); // delete-key at same seq as put masks it
        Assert.Null(gy); // newer range at [x,z) masks older put of y
    }
}
