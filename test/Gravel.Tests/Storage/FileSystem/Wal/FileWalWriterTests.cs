using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Xunit;

namespace Gravel.Storage.FileSystem.Wal;

public class FileWalWriterTests : IAsyncLifetime
{
    string _dir = string.Empty;

    public Task InitializeAsync()
    {
        // Arrange temp wal directory
        _dir = Path.Combine(Path.GetTempPath(), "gravel-test-wal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
        catch
        {
            // Best-effort cleanup of temp directory
        }

        return Task.CompletedTask;
    }

    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task should_replay_single_transaction_given_put_commit_when_reader_replays()
    {
        // Act
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(42);

            var entry = DbEntry.Put(B("key1"), B("value1"), 0);
            await w.AppendAsync(42, entry);

            await w.CommitTransactionAsync(42);

            // Assert (in-memory state)
            w.LastSequence.Should().Be(1ul);
        }

        // Assert (on-disk replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Should().HaveCount(3);

        records[0].Type.Should().Be(WalConstants.RecordBeginTxn);
        records[0].TxnId.Should().Be(42ul);

        records[1].Type.Should().Be(WalConstants.RecordEntry);
        records[1].TxnId.Should().Be(42ul);
        records[1].Entry.Should().NotBeNull();
        records[1].Entry?.Sequence.Should().Be(1ul);
        Encoding.UTF8.GetString(records[1].Entry?.Key.ToArray() ?? []).Should().Be("key1");
        Encoding.UTF8.GetString(records[1].Entry?.Value.ToArray() ?? []).Should().Be("value1");

        records[2].Type.Should().Be(WalConstants.RecordCommitTxn);
        records[2].TxnId.Should().Be(42ul);
    }

    [Fact]
    public async Task should_replay_deletes_given_delete_key_and_delete_range_when_reader_replays()
    {
        // Act
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(100);

            var dkey = DbEntry.DeleteKey(B("dkey"), 0);
            await w.AppendAsync(100, dkey);

            var drange = DbEntry.DeleteRange(B("rstart"), B("rend"), 0);
            await w.AppendAsync(100, drange);

            await w.CommitTransactionAsync(100);

            // Assert (in-memory)
            w.LastSequence.Should().Be(2ul);
        }

        // Assert (replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Should().HaveCount(4);

        records[1].Type.Should().Be(WalConstants.RecordEntry);
        records[1].TxnId.Should().Be(100ul);
        records[1].Entry.Should().NotBeNull();
        var dbEntry = records[1].Entry!.Value;
        dbEntry.Key.ToArray().Should().BeEquivalentTo(B("dkey"));
        records[1].Entry?.Value.ToArray().Should().BeEmpty();

        records[2].Type.Should().Be(WalConstants.RecordEntry);
        records[2].Entry.Should().NotBeNull();
        records[2].Entry!.Value.Key.ToArray().Should().BeEquivalentTo(B("rstart"));
        records[2].Entry?.Value.ToArray().Should().BeEquivalentTo(B("rend"));
    }

    [Fact]
    public async Task should_roll_segments_when_size_exceeded_given_small_segment_limit_when_writing()
    {
        // Act
        await using (var w = new FileWalWriter(_dir, 64))
        {
            for (var t = 1; t <= 5; t++)
            {
                await w.BeginTransactionAsync((ulong)t);
                var entry = DbEntry.Put(B("k" + t), B(new string('v', 20)), 0);
                await w.AppendAsync((ulong)t, entry);
                await w.CommitTransactionAsync((ulong)t);
            }

            // Assert (in-memory)
            w.LastSequence.Should().Be(5ul);
        }

        // Assert (files and replay)
        var files = Directory.GetFiles(_dir, "*.wal").OrderBy(f => f).ToList();
        files.Count.Should().BeGreaterThan(1);

        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Count.Should().Be(5 * 3);

        var entrySeqs = records.Where(r => r.Type == WalConstants.RecordEntry).Select(r => r.Entry!.Value.Sequence)
            .ToList();
        entrySeqs.Should().BeInAscendingOrder();
        entrySeqs.Should().HaveCount(5);
        entrySeqs.Distinct().Count().Should().Be(5);
    }

    [Fact]
    public async Task should_handle_empty_key_and_empty_value_given_put_and_replay()
    {
        // Act
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(7);

            var entry = DbEntry.Put(B(""), B(""), 0);
            await w.AppendAsync(7, entry);

            await w.CommitTransactionAsync(7);

            // Assert (in-memory)
            w.LastSequence.Should().Be(1ul);
        }

        // Assert (replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Should().HaveCount(3);
        var e = records.Single(r => r.Type == WalConstants.RecordEntry);
        e.Entry.Should().NotBeNull();
        e.Entry!.Value.Key.ToArray().Should().BeEmpty();
        e.Entry.Value.Value.ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task should_write_rollback_given_transaction_rollback_when_replayed()
    {
        // Act
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(9);

            var entry = DbEntry.Put(B("rk"), B("rv"), 0);
            await w.AppendAsync(9, entry);

            await w.RollbackTransactionAsync(9);

            // Assert (in-memory)
            w.LastSequence.Should().Be(1ul);
        }

        // Assert (replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        // Expect begin, entry, rollback
        records.Should().HaveCount(3);
        records[2].Type.Should().Be(WalConstants.RecordRollbackTxn);
        records[2].TxnId.Should().Be(9ul);
    }
}