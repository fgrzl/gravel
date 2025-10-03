using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Engine.Compaction;
using Gravel.Storage.InMemory.Sst;
using Gravel.Storage.InMemory.Wal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Engine;

public class EngineCompactionVisibilityTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static IGravelEngine CreateEngine(
        InMemorySstFactory sstFactory,
        InMemoryWalFactory walFactory,
        GravelOptions? opts = null)
    {
        var options = Options.Create(opts ?? new GravelOptions
        {
            DatabasePath = "mem://db",
            MemTableThreshold = 1, // flush every put
            SstLevels = 3,
            CompactionFanInThreshold = 1, // trigger compaction on any file
            WalSyncOnCommit = true
        });

        // Use real worker; compaction runs asynchronously
        var worker = new CompactionWorker(double.MaxValue);
        return new Engine(options, walFactory, sstFactory, worker, NullLogger<Engine>.Instance);
    }

    static InMemorySstFactory SstFactory()
    {
        return new InMemorySstFactory(Options.Create(new InMemorySstOptions()));
    }

    static InMemoryWalFactory WalFactory()
    {
        return new InMemoryWalFactory(Options.Create(new InMemoryWalOptions()));
    }

    [Fact]
    public async Task should_keep_data_visible_while_compaction_is_pending()
    {
        // Arrange
        var sst = SstFactory();
        var wal = WalFactory();
        await using var eng = CreateEngine(sst, wal, new GravelOptions
        {
            DatabasePath = "mem://vtest",
            MemTableThreshold = 1,
            SstLevels = 2,
            CompactionFanInThreshold = 1, // compaction scheduled immediately after flush
            WalSyncOnCommit = true
        });

        // Act: write a key which forces flush and immediate compaction scheduling
        await eng.PutAsync(B("a"), B("va"));

        // Immediately query before background compaction can finish
        var got = await eng.GetAsync(B("a"));

        // Assert: value should still be visible
        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(B("va").ToArray());
    }
}