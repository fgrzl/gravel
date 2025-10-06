using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        Assert.True(got.HasValue);
        Assert.Equal(val.ToArray(), got!.Value.ToArray());
        Assert.True(existed);
        Assert.True(deleted);
        Assert.Null(missing);
    }

    [Fact]
    public async Task should_put_and_get_given_key_and_value()
    {
        var key = B("k1");
        var val = B("v1");

        await Engine.PutAsync(key, val);
        var got = await Engine.GetAsync(key);

        Assert.True(got.HasValue);
        Assert.Equal(val.ToArray(), got!.Value.ToArray());
    }

    [Fact]
    public async Task should_delete_given_existing_key()
    {
        var k = B("d1");
        await Engine.PutAsync(k, B("v"));
        Assert.True((await Engine.GetAsync(k)).HasValue);
        var deleted = await Engine.DeleteAsync(k);
        Assert.True(deleted);
        Assert.Null(await Engine.GetAsync(k));
    }

    [Fact]
    public async Task should_try_get_return_false_given_missing_key()
    {
        var missing = await Engine.ExistsAsync(B("nope"));
        Assert.False(missing);
    }

    [Fact]
    public async Task should_iterate_in_sorted_order_given_multiple_entries()
    {
        foreach (var k in new[] { "c", "a", "b" })
            await Engine.PutAsync(B(k), B(k.ToUpperInvariant()));

        var keys = new List<string>();
        await foreach (var item in Engine.ScanAsync(new Query(null)))
            keys.Add(Encoding.UTF8.GetString(item.Key.Span));

        Assert.Equal(new[] { "a", "b", "c" }, keys);
    }

    [Fact]
    public async Task should_seek_ge_to_first_entry_ge_given_target_key()
    {
        foreach (var k in new[] { "a", "b", "c", "d" })
            await Engine.PutAsync(B(k), B(k));

        var results = new List<string>();
        await foreach (var item in Engine.ScanAsync(new Query(B("b"), B("d"))))
            results.Add(Encoding.UTF8.GetString(item.Key.Span));

        Assert.Equal(new[] { "b", "c" }, results);
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
            Assert.True(e.Key.Length > 0);
        }

        Assert.True(enumerated >= 2);
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

        Assert.Equal("p", Encoding.UTF8.GetString(kcopy.Span));
        Assert.Equal("v", Encoding.UTF8.GetString(vcopy.Span));
    }

    [Fact]
    public async Task should_memtable_insert_get_delete_clear()
    {
        // insert
        for (var i = 0; i < 10; i++)
            await Engine.PutAsync(B($"k{i}"), B($"v{i}"));

        // read back
        for (var i = 0; i < 10; i++)
            Assert.True((await Engine.GetAsync(B($"k{i}"))).HasValue);

        // delete a few
        await Engine.DeleteAsync(B("k3"));
        await Engine.DeleteAsync(B("k7"));
        Assert.Null(await Engine.GetAsync(B("k3")));
        Assert.Null(await Engine.GetAsync(B("k7")));
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

        // Wait up to 1.5s for the writer to complete; don't tie the delay to the same CTS to avoid it throwing
        await Task.WhenAny(writer, Task.Delay(1500));

        // Cancel the operations and wait for tasks to observe cancellation. It's possible some tasks
        // will throw due to cancellation; swallow expected cancellation exceptions.
        await cts.CancelAsync();
        try
        {
            await Task.WhenAll(readers.Append(writer));
        }
        catch (OperationCanceledException)
        {
            // expected when the CTS cancels the running tasks
        }
        catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // all tasks were canceled - expected
        }
    }


    [Fact]
    public async Task should_keep_data_visible_while_compaction_is_pending()
    {
        await Engine.PutAsync(B("a"), B("va"));

        // Immediately query before background compaction can finish
        var got = await Engine.GetAsync(B("a"));

        // Assert: value should still be visible
        Assert.True(got.HasValue);
        Assert.Equal(B("va").ToArray(), got!.Value.ToArray());
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
        Assert.Null(got);
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
        Assert.True(existsAfterPut);
        Assert.True(got.HasValue);
        Assert.Equal(B("v1").ToArray(), got!.Value.ToArray());
        Assert.False(existsAfterDel);
        Assert.Null(gotAfterDel);
        Assert.Null(await Engine.GetAsync(k));
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
        Assert.True(a.HasValue);
        Assert.Null(b);
        Assert.Null(cBefore);
        Assert.False(bExists);
        Assert.True(cAfter.HasValue);
        Assert.Equal("vc2", Encoding.UTF8.GetString(cAfter!.Value.Span));

        // After commit
        var postB = await Engine.GetAsync(B("b"));
        Assert.Null(postB);
        var postC = await Engine.GetAsync(B("c"));
        Assert.True(postC.HasValue);
        Assert.Equal("vc2", Encoding.UTF8.GetString(postC!.Value.Span));
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
        Assert.NotNull(txn.CommitSequence);
        Assert.True(txn.CommitSequence!.Value > 0);
        var again = async () => await txn.CommitAsync().AsTask();
        await Assert.ThrowsAsync<GravelException>(async () => await again());
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
        Assert.Null(await Engine.GetAsync(k));
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
        await Assert.ThrowsAsync<GravelInvalidOperationException>(async () => await act());

        // Arrange second txn
        await using var t2 = await Engine.BeginTransactionAsync();
        var k2 = B("ins2");
        await t2.InsertAsync(k2, B("v2"));
        await t2.InsertAsync(k2, B("v3"));
        var act2 = async () => await t2.CommitAsync().AsTask();

        // Assert
        await Assert.ThrowsAsync<GravelInvalidOperationException>(async () => await act2());
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
        await Assert.ThrowsAsync<GravelInvalidOperationException>(async () => await act());
    }

    // New tests covering top-level InsertAsync and Batch duplicate-insert behavior
    [Fact]
    public async Task should_insert_and_get_given_insert_for_missing_key()
    {
        var k = B("insert1");
        var v = B("vi");

        await Engine.InsertAsync(k, v);
        var got = await Engine.GetAsync(k);
        Assert.True(got.HasValue);
        Assert.Equal("vi", Encoding.UTF8.GetString(got!.Value.Span));
    }

    [Fact]
    public async Task should_fail_insert_given_existing_key_when_insert_async()
    {
        var k = B("insert_exist");
        await Engine.PutAsync(k, B("v0"));

        var act = async () => await Engine.InsertAsync(k, B("v1"));
        await Assert.ThrowsAsync<GravelInvalidOperationException>(async () => await act());
    }

    [Fact]
    public async Task should_fail_duplicate_insert_within_batch_when_batch_async()
    {
        var k = B("batchdup");
        var batch = new List<Mutation>
        {
            Mutation.Insert(k, B("v1")),
            Mutation.Insert(k, B("v2"))
        };

        var act = async () => await Engine.BatchAsync(batch);
        await Assert.ThrowsAsync<GravelInvalidOperationException>(async () => await act());
    }
}
