using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Engine;
using Gravel.Engine.Compaction;
using Gravel.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gravel;

public class GravelBehaviorTests
{
    static ReadOnlyMemory<byte> B(string s) => Encoding.UTF8.GetBytes(s);

    static IDbEngine CreateEngine(ISstFactory sstFactory, IWalFactory walFactory, GravelOptions? opts = null)
    {
        var options = Options.Create(opts ?? new GravelOptions
        {
            DatabasePath = "mem://db",
            MemTableThreshold = 100,
            SstLevels = 3,
            WalSyncOnCommit = true
        });

        var worker = new CompactionWorker(double.MaxValue);
        return new DbEngine(options, walFactory, sstFactory, worker, NullLogger<Engine>.Instance);
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_put_and_get_given_key_and_value(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory);
        await eng.InitializeAsync();

        var key = B("k1");
        var val = B("v1");

        await eng.PutAsync(key, val);
        var got = await eng.GetAsync(key);

        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(val.ToArray());

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_delete_given_existing_key(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory);
        await eng.InitializeAsync();

        var k = B("d1");
        await eng.PutAsync(k, B("v"));
        (await eng.GetAsync(k)).HasValue.Should().BeTrue();
        var deleted = await eng.DeleteAsync(k);
        deleted.Should().BeTrue();
        (await eng.GetAsync(k)).Should().BeNull();

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_tryget_return_false_given_missing_key(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory);
        await eng.InitializeAsync();

        var missing = await eng.ExistsAsync(B("nope"));
        missing.Should().BeFalse();

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_iterate_in_sorted_order_given_multiple_entries(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory);
        await eng.InitializeAsync();

        foreach (var k in new[] { "c", "a", "b" })
            await eng.PutAsync(B(k), B(k.ToUpperInvariant()));

        var keys = new List<string>();
        await foreach (var item in eng.ScanAsync(new Query(null, null)))
            keys.Add(Encoding.UTF8.GetString(item.Key.Span));

        keys.Should().Equal("a", "b", "c");

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_seekge_to_first_entry_ge_given_target_key(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory);
        await eng.InitializeAsync();

        foreach (var k in new[] { "a", "b", "c", "d" })
            await eng.PutAsync(B(k), B(k));

        var results = new List<string>();
        await foreach (var item in eng.ScanAsync(new Query(B("b"), B("d"))))
            results.Add(Encoding.UTF8.GetString(item.Key.Span));

        results.Should().Equal("b", "c");

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_iterator_next_and_valid_behavior(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory);
        await eng.InitializeAsync();

        await eng.PutAsync(B("x"), B("1"));
        await eng.PutAsync(B("y"), B("2"));

        var enumerated = 0;
        await foreach (var e in eng.ScanAsync(new Query(null, null)))
        {
            enumerated++;
            e.Key.Length.Should().BeGreaterThan(0);
        }

        enumerated.Should().BeGreaterOrEqualTo(2);

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_return_key_and_value_memory_lifetimes_given_iterator_operations(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory);
        await eng.InitializeAsync();

        await eng.PutAsync(B("p"), B("v"));

        ReadOnlyMemory<byte> kcopy = default;
        ReadOnlyMemory<byte> vcopy = default;

        await foreach (var e in eng.ScanAsync(new Query(null, null)))
        {
            kcopy = e.Key;
            vcopy = e.Value;
            break;
        }

        Encoding.UTF8.GetString(kcopy.Span).Should().Be("p");
        Encoding.UTF8.GetString(vcopy.Span).Should().Be("v");

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_memtable_insert_get_delete_clear(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory, new GravelOptions { DatabasePath = "mem://mt", MemTableThreshold = 1000 });
        await eng.InitializeAsync();

        // insert
        for (var i = 0; i < 10; i++)
            await eng.PutAsync(B($"k{i}"), B($"v{i}"));

        // read back
        for (var i = 0; i < 10; i++)
            (await eng.GetAsync(B($"k{i}"))).HasValue.Should().BeTrue();

        // delete a few
        await eng.DeleteAsync(B("k3"));
        await eng.DeleteAsync(B("k7"));
        (await eng.GetAsync(B("k3"))).Should().BeNull();
        (await eng.GetAsync(B("k7"))).Should().BeNull();

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [MemberData(nameof(EngineTestProviders.AllEngineProviders), MemberType = typeof(EngineTestProviders))]
    public async Task should_allow_concurrent_readers_single_writer(string mode, ISstFactory sstFactory, IWalFactory walFactory, string tempDir)
    {
        await using var eng = CreateEngine(sstFactory, walFactory, new GravelOptions { DatabasePath = "mem://conc", MemTableThreshold = 1000 });
        await eng.InitializeAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var writer = Task.Run(async () =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested && i < 500)
            {
                await eng.PutAsync(B($"k{i}"), B("v"));
                i++;
                await Task.Yield();
            }
        }, cts.Token);

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await foreach (var _ in eng.ScanAsync(new Query(null, null)))
                {
                    // iterate to ensure no exceptions
                }
                await Task.Yield();
            }
        }, cts.Token)).ToArray();

        await Task.WhenAny(writer, Task.Delay(1500));
        cts.Cancel();
        await Task.WhenAll(readers.Append(writer));

        if (!string.IsNullOrEmpty(tempDir)) { try { System.IO.Directory.Delete(tempDir, true); } catch { } }
    }
}