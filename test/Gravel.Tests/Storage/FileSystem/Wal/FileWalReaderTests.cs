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

public class FileWalReaderTests : IAsyncLifetime
{
    string _dir = string.Empty;

    public Task InitializeAsync()
    {
        // Arrange temp directory
        _dir = Path.Combine(Path.GetTempPath(), "gravel-test-wal-reader-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup
        }

        return Task.CompletedTask;
    }

    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task should_replay_single_transaction_given_put_commit_when_replay_async()
    {
        // Arrange

        // Act
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(42);
            var entry = DbEntry.Put(B("key1"), B("value1"), 0);
            await w.AppendAsync(42, entry);
            await w.CommitTransactionAsync(42);
        }

        // Assert
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Should().HaveCount(3);
        records[0].Type.Should().Be(WalConstants.RecordBeginTxn);
        records[0].TxnId.Should().Be(42ul);

        records[1].Type.Should().Be(WalConstants.RecordEntry);
        records[1].TxnId.Should().Be(42ul);
        records[1].Entry.Should().NotBeNull();
        var dbEntry = records[1].Entry!.Value;
        dbEntry.Sequence.Should().Be(1ul);
        Encoding.UTF8.GetString(dbEntry.Key.ToArray()).Should().Be("key1");
        Encoding.UTF8.GetString(dbEntry.Value.ToArray()).Should().Be("value1");

        records[2].Type.Should().Be(WalConstants.RecordCommitTxn);
        records[2].TxnId.Should().Be(42ul);
    }

    [Fact]
    public async Task should_replay_delete_key_and_delete_range_given_entries_when_replay_async()
    {
        // Arrange

        // Act
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(100);

            var dkey = DbEntry.DeleteKey(B("dkey"), 0);
            await w.AppendAsync(100, dkey);

            var drange = DbEntry.DeleteRange(B("rstart"), B("rend"), 0);
            await w.AppendAsync(100, drange);

            await w.CommitTransactionAsync(100);
        }

        // Assert
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Should().HaveCount(4);

        var entry1 = records[1];
        entry1.Type.Should().Be(WalConstants.RecordEntry);
        entry1.TxnId.Should().Be(100ul);
        entry1.Entry.Should().NotBeNull();
        var db1 = entry1.Entry!.Value;
        db1.Key.ToArray().Should().BeEquivalentTo(B("dkey"));
        db1.Value.ToArray().Should().BeEmpty();

        var entry2 = records[2];
        entry2.Type.Should().Be(WalConstants.RecordEntry);
        entry2.Entry.Should().NotBeNull();
        var db2 = entry2.Entry!.Value;
        db2.Key.ToArray().Should().BeEquivalentTo(B("rstart"));
        db2.Value.ToArray().Should().BeEquivalentTo(B("rend"));
    }

    [Fact]
    public async Task should_replay_multiple_segments_in_order_given_small_segment_limit_when_replay_async()
    {
        // Arrange

        // Act
        await using (var w = new FileWalWriter(_dir, 128))
        {
            for (var t = 1; t <= 8; t++)
            {
                await w.BeginTransactionAsync((ulong)t);
                var entry = DbEntry.Put(B("k" + t), B(new string('v', 30)), 0);
                await w.AppendAsync((ulong)t, entry);
                await w.CommitTransactionAsync((ulong)t);
            }
        }

        // Assert
        var files = Directory.GetFiles(_dir, "*.wal").OrderBy(f => f).ToList();
        files.Count.Should().BeGreaterThan(1);

        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Count.Should().Be(8 * 3);

        var entrySeqs = records.Where(r => r.Type == WalConstants.RecordEntry).Select(r => r.Entry!.Value.Sequence)
            .ToList();
        entrySeqs.Should().BeInAscendingOrder();
        entrySeqs.Should().HaveCount(8);
        entrySeqs.Distinct().Count().Should().Be(8);
    }

    [Fact]
    public async Task should_handle_empty_key_and_value_given_put_when_replay_async()
    {
        // Arrange

        // Act
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(7);

            var entry = DbEntry.Put(B(""), B(""), 0);
            await w.AppendAsync(7, entry);

            await w.CommitTransactionAsync(7);
        }

        // Assert
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        records.Should().HaveCount(3);
        var e = records.Single(r => r.Type == WalConstants.RecordEntry);
        e.Entry.Should().NotBeNull();
        var db = e.Entry!.Value;
        db.Key.ToArray().Should().BeEmpty();
        db.Value.ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task should_stop_replay_given_corrupt_segment_when_replay_async()
    {
        // Arrange: create a valid first segment
        await using (var w = new FileWalWriter(_dir, 1024))
        {
            await w.BeginTransactionAsync(1);
            var entry = DbEntry.Put(B("a"), B("b"), 0);
            await w.AppendAsync(1, entry);
            await w.CommitTransactionAsync(1);
        }

        // Act: write a second corrupt segment file
        var corruptPath = Path.Combine(_dir, string.Format("{0:D20}.wal", 2ul));
        await File.WriteAllBytesAsync(corruptPath, [0xFF]); // unknown record type

        // Assert
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        // reader should return records from first segment only and stop when hitting corrupt second
        records.Should().NotBeEmpty();
        records.All(r => r.TxnId == 1ul || (r.Entry is not null && r.Entry.Value.Sequence == 1ul)).Should()
            .BeTrue();

        // ensure it did not include any records from a hypothetical second segment
        records.Select(r => r.TxnId).Distinct().Should().ContainSingle(x => x == 1ul);
    }
}