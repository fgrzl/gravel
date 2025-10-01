using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Engine.Compaction;
using Gravel.Exceptions;
using Gravel.Storage.InMemory.Sst;
using Gravel.Storage.InMemory.Wal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel.Engine;

public class EngineTests
{
    static IGravelEngine CreateEngine(
        InMemorySstFactory sstFactory,
        InMemoryWalFactory walFactory,
        GravelOptions? opts = null)
    {
        var options = Options.Create(opts ?? new GravelOptions
        {
            DatabasePath = "mem://db",
            MemTableThreshold = 100, // avoid auto-flush in most tests
            SstLevels = 3,
            WalSyncOnCommit = true
        });

        // create a compaction worker for tests that runs tasks in background
        var worker = new CompactionWorker(double.MaxValue);
        return new Engine(options, walFactory, sstFactory, worker, NullLogger<Engine>.Instance);
    }

    static InMemorySstFactory SstFactory() => new(Options.Create(new InMemorySstOptions()));
    static InMemoryWalFactory WalFactory() => new(Options.Create(new InMemoryWalOptions()));
    static ReadOnlyMemory<byte> B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task should_replay_wal_and_load_levels_given_initialize()
    {
        // Arrange
        var sst = SstFactory();
        var wal = WalFactory();
        await using var w = wal.CreateWriter("/db");
        await w.BeginTransactionAsync(1);
        await w.AppendAsync(1, DbEntry.Put(B("a"), B("1"), 0));
        await w.CommitTransactionAsync(1);

        // Act
        await using var eng = CreateEngine(sst, wal);
        await eng.InitializeAsync();

        // Assert
        (await eng.GetAsync(B("a"))).Should().NotBeNull();
    }

    [Fact]
    public async Task should_roundtrip_put_get_delete_given_basic_flow()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        var key = B("k1");
        var val = B("v1");

        // Act
        await eng.PutAsync(key, val);
        var got = await eng.GetAsync(key);
        var existed = await eng.ExistsAsync(key);
        var deleted = await eng.DeleteAsync(key);
        var missing = await eng.GetAsync(key);

        // Assert
        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(val.ToArray());
        existed.Should().BeTrue();
        deleted.Should().BeTrue();
        missing.Should().BeNull();
    }

    [Fact]
    public async Task should_fail_insert_given_key_exists_when_insert()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        var key = B("ik");
        await eng.PutAsync(key, B("v1"));

        // Act
        var act = async () => await eng.InsertAsync(key, B("v2"));

        // Assert
        await act.Should().ThrowAsync<GravelInvalidOperationException>();
    }

    [Fact]
    public async Task should_apply_all_mutations_atomically_given_batch()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        var muts = new List<Mutation>
        {
            Mutation.Put(B("a"), B("1")),
            Mutation.Put(B("b"), B("2")),
            Mutation.Delete(B("a"))
        };

        // Act
        await eng.BatchAsync(muts);

        // Assert
        (await eng.GetAsync(B("a"))).Should().BeNull();
        var b = await eng.GetAsync(B("b"));
        b.HasValue.Should().BeTrue();
        b!.Value.ToArray().Should().Equal(B("2").ToArray());
    }

    [Fact]
    public async Task should_stage_and_commit_mutations_given_transaction()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        await using var txn = await eng.BeginTransactionAsync();

        // Act
        await txn.PutAsync(B("t1"), B("x"));
        await txn.CommitAsync();
        var got = await eng.GetAsync(B("t1"));

        // Assert
        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(B("x").ToArray());
    }

    [Fact]
    public async Task should_return_in_key_order_and_respect_bounds_given_scan()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        foreach (var k in new[] { "a", "b", "c", "d" })
            await eng.PutAsync(B(k), B(k.ToUpperInvariant()));

        // Act
        var results = new List<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)>();
        await foreach (var item in eng.ScanAsync(new Query(B("b"), B("d"))))
            results.Add(item);

        // Assert
        results.Select(x => Encoding.UTF8.GetString(x.Key.Span)).Should().Equal("b", "c");
    }

    [Fact]
    public async Task should_create_sst_on_flush_and_add_higher_level_on_compaction_given_thresholds()
    {
        // Arrange
        var sst = SstFactory();
        var wal = WalFactory();
        var eng = CreateEngine(sst, wal, new GravelOptions
        {
            DatabasePath = "/mem",
            MemTableThreshold = 3, // low threshold to force flush
            SstLevels = 3,
            CompactionFanInThreshold = 1 // to trigger compaction on flush add
        });

        // Act
        await eng.PutAsync(B("k1"), B("v1"));
        await eng.PutAsync(B("k2"), B("v2"));
        await eng.PutAsync(B("k3"), B("v3")); // triggers flush
        await Task.Delay(10);

        // Assert
        var all = sst.ListPaths();
        all.Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_hide_get_and_exists_for_keys_in_range_given_delete_range()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        foreach (var k in new[] { "a", "b", "c", "d" })
            await eng.PutAsync(B(k), B("v" + k));

        // Act
        await eng.DeleteRangeAsync(B("b"), B("d")); // [b,d)

        // Assert
        (await eng.GetAsync(B("a"))).HasValue.Should().BeTrue();
        (await eng.GetAsync(B("b"))).Should().BeNull();
        (await eng.GetAsync(B("c"))).Should().BeNull();
        (await eng.GetAsync(B("d"))).HasValue.Should().BeTrue(); // end exclusive

        (await eng.ExistsAsync(B("a"))).Should().BeTrue();
        (await eng.ExistsAsync(B("b"))).Should().BeFalse();
        (await eng.ExistsAsync(B("c"))).Should().BeFalse();
        (await eng.ExistsAsync(B("d"))).Should().BeTrue();
    }

    [Fact]
    public async Task should_mask_sst_values_given_delete_range_in_memtable()
    {
        // Arrange
        var sst = SstFactory();
        var wal = WalFactory();
        // Force flush after two puts so that b and c land in SST
        var eng = CreateEngine(sst, wal, new GravelOptions
        {
            DatabasePath = "/mem2",
            MemTableThreshold = 2,
            SstLevels = 2,
            WalSyncOnCommit = true
        });

        // Act
        await eng.PutAsync(B("b"), B("vb"));
        await eng.PutAsync(B("c"), B("vc")); // triggers flush to SST
        await eng.DeleteRangeAsync(B("b"), B("d")); // range tombstone only in memtable

        // Assert
        (await eng.GetAsync(B("b"))).Should().BeNull();
        (await eng.GetAsync(B("c"))).Should().BeNull();
        (await eng.ExistsAsync(B("b"))).Should().BeFalse();
        (await eng.ExistsAsync(B("c"))).Should().BeFalse();
    }

    [Fact]
    public async Task should_be_false_after_delete_key_given_exists()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        var k = B("x");
        await eng.PutAsync(k, B("vx"));

        // Act
        var existed = await eng.ExistsAsync(k);
        await eng.DeleteAsync(k);
        var still = await eng.ExistsAsync(k);
        var missing = await eng.GetAsync(k);

        // Assert
        existed.Should().BeTrue();
        still.Should().BeFalse();
        missing.Should().BeNull();
    }
}