using System;
using System.Reflection;
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

    [Fact]
    public async Task get_should_not_enumerate_memtable_scan_for_range_check()
    {
        // Arrange: large number of non-range entries; range query should use index, not scan
        var sst = new InMemorySstFactory(Options.Create(new InMemorySstOptions()));
        var wal = new InMemoryWalFactory(Options.Create(new InMemoryWalOptions()));
        var eng = CreateEngine(sst, wal);
        await eng.InitializeAsync();

        // seed many keys
        for (var i = 0; i < 2000; i++)
            await eng.PutAsync(B($"k{i:D4}"), B("v"));

        // add one range tombstone
        await eng.DeleteRangeAsync(B("a"), B("zzzz"));

        // Act
        // Capture memtable from dbEngine via reflection-free route by starting a scan to identify same instance
        // Instead, we can infer behavior by calling Get and checking that Scan() wasn't enumerated
        // We rely on internal diagnostic counter ScanEnumerations in MemTable
        var memTableField = typeof(DbEngine).GetField("_memTable", BindingFlags.NonPublic | BindingFlags.Instance);
        memTableField.Should().NotBeNull();
        var mem = (MemTable)memTableField!.GetValue(eng)!;
        var before = mem.ScanEnumerations;

        // Call Get for a key that will be masked by range
        var got = await eng.GetAsync(B("k1000"));

        var after = mem.ScanEnumerations;

        // Assert: no scan enumeration should have occurred
        after.Should().Be(before);
        got.Should().BeNull();
    }
}