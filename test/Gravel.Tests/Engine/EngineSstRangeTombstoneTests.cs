using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Engine.Compaction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Engine;

public class EngineSstRangeTombstoneTests
{
    static ReadOnlyMemory<byte> B(string s) => Encoding.UTF8.GetBytes(s);

    static Engine CreateEngineWithFactory(TestSstFactory factory)
    {
        var opts = Options.Create(new GravelOptions
        {
            DatabasePath = "/x",
            MemTableThreshold = 100,
            SstLevels = 2,
            WalSyncOnCommit = true
        });
        // WAL unused in this test; using in-memory implementation
        var walFactory = new Storage.InMemory.Wal.InMemoryWalFactory(Options.Create(new Storage.InMemory.Wal.InMemoryWalOptions()))
            as Abstractions.Storage.Wal.IWalFactory;
        var worker = new CompactionWorker(double.MaxValue);
        return new Engine(opts, walFactory, factory, worker, NullLogger<Engine>.Instance);
    }

    [Fact]
    public async Task should_mask_get_with_sst_range_tombstone_even_when_no_exact_entry()
    {
        // Arrange: L1 has a range delete covering [b,d) with higher seq than a put in L0
        var factory = new TestSstFactory();
        factory.Register("/x/sst", 1, "0001.sst", new[]
        {
            DbEntry.DeleteRange(B("b"), B("d"), 20UL)
        });
        factory.Register("/x/sst", 0, "0000.sst", new[]
        {
            DbEntry.Put(B("c"), B("vc"), 10UL)
        });

        var eng = CreateEngineWithFactory(factory);
        await eng.InitializeAsync();

        // Act
        var got = await eng.GetAsync(B("c"));

        // Assert: masked by newer range delete in L1
        got.Should().BeNull();
    }
}
