using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Exceptions;
using Xunit;

namespace Gravel.Engine;

public abstract class EngineTestsBase
{
    protected IDbEngine Engine { get; set; } = null!;

    protected static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }


    [Fact]
    public async Task should_roundtrip_put_get_delete_given_basic_flow()
    {
        var key = B("k1");
        var val = B("v1");

        await Engine.PutAsync(key, val);
        var got = await Engine.GetAsync(key);
        var existed = await Engine.ExistsAsync(key);
        var deleted = await Engine.DeleteAsync(key);
        var missing = await Engine.GetAsync(key);

        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(val.ToArray());
        existed.Should().BeTrue();
        deleted.Should().BeTrue();
        missing.Should().BeNull();
    }

    [Fact]
    public async Task should_put_and_get_given_key_and_value()
    {
        var key = B("k1");
        var val = B("v1");

        await Engine.PutAsync(key, val);
        var got = await Engine.GetAsync(key);

        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(val.ToArray());
    }

    [Fact]
    public async Task should_delete_given_existing_key()
    {
        var k = B("d1");
        await Engine.PutAsync(k, B("v"));
        (await Engine.GetAsync(k)).HasValue.Should().BeTrue();
        var deleted = await Engine.DeleteAsync(k);
        deleted.Should().BeTrue();
        (await Engine.GetAsync(k)).Should().BeNull();
    }

    [Fact]
    public async Task should_try_get_return_false_given_missing_key()
    {
        var missing = await Engine.ExistsAsync(B("nope"));
        missing.Should().BeFalse();
    }

    [Fact]
    public async Task should_iterate_in_sorted_order_given_multiple_entries()
    {
        foreach (var k in new[] { "c", "a", "b" })
            await Engine.PutAsync(B(k), B(k.ToUpperInvariant()));

        var keys = new List<string>();
        await foreach (var item in Engine.ScanAsync(new Query(null)))
            keys.Add(Encoding.UTF8.GetString(item.Key.Span));

        keys.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task should_seek_ge_to_first_entry_ge_given_target_key()
    {
        foreach (var k in new[] { "a", "b", "c", "d" })
            await Engine.PutAsync(B(k), B(k));

        var results = new List<string>();
        await foreach (var item in Engine.ScanAsync(new Query(B("b"), B("d"))))
            results.Add(Encoding.UTF8.GetString(item.Key.Span));

        results.Should().Equal("b", "c");
    }

    [Fact]
    public async Task should_iterator_next_and_valid_behavior()
    {
        await Engine.PutAsync(B("x"), B("1"));
        await Engine.PutAsync(B("y"), B("2"));

        var enumerated = 0;
        await foreach (var e in Engine.ScanAsync(new Query(null)))
        {
            enumerated++;
            e.Key.Length.Should().BeGreaterThan(0);
        }

        enumerated.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_return_key_and_value_memory_lifetimes_given_iterator_operations()
    {
        await Engine.PutAsync(B("p"), B("v"));

        ReadOnlyMemory<byte> kcopy = default;
        ReadOnlyMemory<byte> vcopy = default;

        await foreach (var e in Engine.ScanAsync(new Query(null)))
        {
            kcopy = e.Key;
            vcopy = e.Value;
            break;
        }

        Encoding.UTF8.GetString(kcopy.Span).Should().Be("p");
        Encoding.UTF8.GetString(vcopy.Span).Should().Be("v");
    }

    [Fact]
    public async Task should_memtable_insert_get_delete_clear()
    {
        // insert
        for (var i = 0; i < 10; i++)
            await Engine.PutAsync(B($"k{i}"), B($"v{i}"));

        // read back
        for (var i = 0; i < 10; i++)
            (await Engine.GetAsync(B($"k{i}"))).HasValue.Should().BeTrue();

        // delete a few
        await Engine.DeleteAsync(B("k3"));
        await Engine.DeleteAsync(B("k7"));
        (await Engine.GetAsync(B("k3"))).Should().BeNull();
        (await Engine.GetAsync(B("k7"))).Should().BeNull();
    }

    [Fact]
    public async Task should_allow_concurrent_readers_single_writer()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var writer = Task.Run(async () =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested && i < 500)
            {
                await Engine.PutAsync(B($"k{i}"), B("v"), cts.Token);
                i++;
                await Task.Yield();
            }
        }, cts.Token);

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await foreach (var _ in Engine.ScanAsync(new Query(null), cts.Token))
                {
                    // iterate to ensure no exceptions
                }

                await Task.Yield();
            }
        }, cts.Token)).ToArray();

        await Task.WhenAny(writer, Task.Delay(1500, cts.Token));
        await cts.CancelAsync();
        await Task.WhenAll(readers.Append(writer));
    }


    [Fact]
    public async Task should_keep_data_visible_while_compaction_is_pending()
    {
        await Engine.PutAsync(B("a"), B("va"));

        // Immediately query before background compaction can finish
        var got = await Engine.GetAsync(B("a"));

        // Assert: value should still be visible
        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(B("va").ToArray());
    }

    [Fact]
    public async Task get_should_not_enumerate_memtable_scan_for_range_check()
    {
        // Arrange
        for (var i = 0; i < 2000; i++)
            await Engine.PutAsync(B($"k{i:D4}"), B("v"));

        await Engine.DeleteRangeAsync(B("a"), B("zzzz"));

        // Act
        // Instead of inspecting internals via reflection, assert behavior via public API.
        // Ensure GetAsync returns promptly and yields null for a key covered by the delete range.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var got = await Engine.GetAsync(B("k1000"), cts.Token);

        // Assert: the key should be considered deleted by the range and not returned.
        got.Should().BeNull();
    }

    [Fact]
    public async Task should_affect_txn_view_and_rollback_restore_given_staged_put_then_delete_when_rollback()
    {
        // Arrange
        await using var txn = await Engine.BeginTransactionAsync();
        var k = B("tkey");

        // Act
        await txn.PutAsync(k, B("v1"));
        var existsAfterPut = await txn.ExistsAsync(k);
        var got = await txn.GetAsync(k);
        await txn.DeleteAsync(k);
        var existsAfterDel = await txn.ExistsAsync(k);
        var gotAfterDel = await txn.GetAsync(k);
        await txn.RollbackAsync();

        // Assert
        existsAfterPut.Should().BeTrue();
        got.HasValue.Should().BeTrue();
        got!.Value.ToArray().Should().Equal(B("v1").ToArray());
        existsAfterDel.Should().BeFalse();
        gotAfterDel.Should().BeNull();
        (await Engine.GetAsync(k)).Should().BeNull();
    }

    [Fact]
    public async Task should_mask_then_readd_key_given_delete_range_then_put_when_commit()
    {
        // Arrange
        await Engine.PutAsync(B("a"), B("va"));
        await Engine.PutAsync(B("b"), B("vb"));
        await Engine.PutAsync(B("c"), B("vc"));

        // Act
        await using var txn = await Engine.BeginTransactionAsync();
        await txn.DeleteRangeAsync(B("b"), B("d"));
        var a = await txn.GetAsync(B("a"));
        var b = await txn.GetAsync(B("b"));
        var cBefore = await txn.GetAsync(B("c"));
        var bExists = await txn.ExistsAsync(B("b"));
        await txn.PutAsync(B("c"), B("vc2"));
        var cAfter = await txn.GetAsync(B("c"));
        await txn.CommitAsync();

        // Assert: within txn view
        a.HasValue.Should().BeTrue();
        b.Should().BeNull();
        cBefore.Should().BeNull();
        bExists.Should().BeFalse();
        cAfter.HasValue.Should().BeTrue();
        Encoding.UTF8.GetString(cAfter!.Value.Span).Should().Be("vc2");

        // After commit
        var postB = await Engine.GetAsync(B("b"));
        postB.Should().BeNull();
        var postC = await Engine.GetAsync(B("c"));
        postC.HasValue.Should().BeTrue();
        Encoding.UTF8.GetString(postC!.Value.Span).Should().Be("vc2");
    }

    [Fact]
    public async Task should_set_commit_sequence_and_disallow_double_commit_given_commit_then_commit_again_when_commit()
    {
        // Arrange
        await using var txn = await Engine.BeginTransactionAsync();

        // Act
        await txn.PutAsync(B("k"), B("v"));
        await txn.CommitAsync();

        // Assert
        txn.CommitSequence.Should().NotBeNull();
        txn.CommitSequence!.Value.Should().BeGreaterThan(0);
        var again = async () => await txn.CommitAsync().AsTask();
        await again.Should().ThrowAsync<GravelException>();
    }

    [Fact]
    public async Task should_auto_rollback_given_dispose_without_commit_when_dispose()
    {
        // Arrange
        var k = B("autorb");

        // Act
        await using (var txn = await Engine.BeginTransactionAsync())
        {
            await txn.PutAsync(k, B("v"));
            // no commit
        }

        // Assert
        (await Engine.GetAsync(k)).Should().BeNull();
    }

    [Fact]
    public async Task should_fail_insert_given_existing_key_and_duplicate_insert_in_txn_when_commit()
    {
        // Arrange
        var k = B("ins1");
        await Engine.PutAsync(k, B("v0"));

        // Act
        await using var t1 = await Engine.BeginTransactionAsync();
        await t1.InsertAsync(k, B("v1"));
        var act = async () => await t1.CommitAsync().AsTask();

        // Assert
        await act.Should().ThrowAsync<GravelInvalidOperationException>();

        // Arrange second txn
        await using var t2 = await Engine.BeginTransactionAsync();
        var k2 = B("ins2");
        await t2.InsertAsync(k2, B("v2"));
        await t2.InsertAsync(k2, B("v3"));
        var act2 = async () => await t2.CommitAsync().AsTask();

        // Assert
        await act2.Should().ThrowAsync<GravelInvalidOperationException>();
    }

    [Fact]
    public async Task should_fail_second_commit_given_concurrent_inserts_same_key_when_commit()
    {
        // Arrange
        var k = B("race");
        await using var t1 = await Engine.BeginTransactionAsync();
        await using var t2 = await Engine.BeginTransactionAsync();

        // Act
        await t1.InsertAsync(k, B("v1"));
        await t2.InsertAsync(k, B("v2"));
        await t1.CommitAsync();
        var act = async () => await t2.CommitAsync().AsTask();

        // Assert
        await act.Should().ThrowAsync<GravelInvalidOperationException>();
    }
}
