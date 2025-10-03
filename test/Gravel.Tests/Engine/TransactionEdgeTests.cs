using System;
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

    [Fact]
    public async Task should_affect_txn_view_and_rollback_restore_given_staged_put_then_delete_when_rollback()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        await using var txn = await eng.BeginTransactionAsync();
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
        (await eng.GetAsync(k)).Should().BeNull();
    }

    [Fact]
    public async Task should_mask_then_readd_key_given_delete_range_then_put_when_commit()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        await eng.PutAsync(B("a"), B("va"));
        await eng.PutAsync(B("b"), B("vb"));
        await eng.PutAsync(B("c"), B("vc"));

        // Act
        await using var txn = await eng.BeginTransactionAsync();
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
        var postB = await eng.GetAsync(B("b"));
        postB.Should().BeNull();
        var postC = await eng.GetAsync(B("c"));
        postC.HasValue.Should().BeTrue();
        Encoding.UTF8.GetString(postC!.Value.Span).Should().Be("vc2");
    }

    [Fact]
    public async Task should_set_commit_sequence_and_disallow_double_commit_given_commit_then_commit_again_when_commit()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        await using var txn = await eng.BeginTransactionAsync();

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
        var eng = CreateEngine(SstFactory(), WalFactory());
        var k = B("autorb");

        // Act
        await using (var txn = await eng.BeginTransactionAsync())
        {
            await txn.PutAsync(k, B("v"));
            // no commit
        }

        // Assert
        (await eng.GetAsync(k)).Should().BeNull();
    }

    [Fact]
    public async Task should_fail_insert_given_existing_key_and_duplicate_insert_in_txn_when_commit()
    {
        // Arrange
        var eng = CreateEngine(SstFactory(), WalFactory());
        var k = B("ins1");
        await eng.PutAsync(k, B("v0"));

        // Act
        await using var t1 = await eng.BeginTransactionAsync();
        await t1.InsertAsync(k, B("v1"));
        var act = async () => await t1.CommitAsync().AsTask();

        // Assert
        await act.Should().ThrowAsync<GravelInvalidOperationException>();

        // Arrange second txn
        await using var t2 = await eng.BeginTransactionAsync();
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
        var eng = CreateEngine(SstFactory(), WalFactory());
        var k = B("race");
        await using var t1 = await eng.BeginTransactionAsync();
        await using var t2 = await eng.BeginTransactionAsync();

        // Act
        await t1.InsertAsync(k, B("v1"));
        await t2.InsertAsync(k, B("v2"));
        await t1.CommitAsync();
        var act = async () => await t2.CommitAsync().AsTask();

        // Assert
        await act.Should().ThrowAsync<GravelInvalidOperationException>();
    }
}