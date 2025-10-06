using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Gravel.TestHelpers;
using Xunit;

namespace Gravel.Storage.FileSystem.Wal;

public class FileWalReaderTests : IAsyncLifetime
{
    string _dir = string.Empty;

    public Task InitializeAsync()
    {
        // Arrange temp directory
        var td = new TempDirectory("gravel-test-wal-reader-");
        _dir = td.Path;
        // store temp dir wrapper in a field via closure? simpler: keep path and ensure cleanup in DisposeAsync
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

    static byte[] B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

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

        Assert.Equal(3, records.Count);
        Assert.Equal(WalConstants.RecordBeginTxn, records[0].Type);
        Assert.Equal(42ul, records[0].TxnId);

        Assert.Equal(WalConstants.RecordEntry, records[1].Type);
        Assert.Equal(42ul, records[1].TxnId);
        Assert.NotNull(records[1].Entry);
        var dbEntry = records[1].Entry!.Value;
        Assert.Equal(1ul, dbEntry.Sequence);
        Assert.Equal("key1", Encoding.UTF8.GetString(dbEntry.Key.ToArray()));
        Assert.Equal("value1", Encoding.UTF8.GetString(dbEntry.Value.ToArray()));

        Assert.Equal(WalConstants.RecordCommitTxn, records[2].Type);
        Assert.Equal(42ul, records[2].TxnId);
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

        Assert.Equal(4, records.Count);

        var entry1 = records[1];
        Assert.Equal(WalConstants.RecordEntry, entry1.Type);
        Assert.Equal(100ul, entry1.TxnId);
        Assert.NotNull(entry1.Entry);
        var db1 = entry1.Entry!.Value;
        Assert.Equal(B("dkey"), db1.Key.ToArray());
        Assert.Empty(db1.Value.ToArray());

        var entry2 = records[2];
        Assert.Equal(WalConstants.RecordEntry, entry2.Type);
        Assert.NotNull(entry2.Entry);
        var db2 = entry2.Entry!.Value;
        Assert.Equal(B("rstart"), db2.Key.ToArray());
        Assert.Equal(B("rend"), db2.Value.ToArray());
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
        Assert.True(files.Count > 1);

        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        Assert.Equal(8 * 3, records.Count);

        var entrySeqs = records.Where(r => r.Type == WalConstants.RecordEntry).Select(r => r.Entry!.Value.Sequence)
            .ToList();
        // check ascending
        for (var i = 1; i < entrySeqs.Count; i++) Assert.True(entrySeqs[i] >= entrySeqs[i - 1]);
        Assert.Equal(8, entrySeqs.Count);
        Assert.Equal(8, entrySeqs.Distinct().Count());
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

        Assert.Equal(3, records.Count);
        var e = records.Single(r => r.Type == WalConstants.RecordEntry);
        Assert.NotNull(e.Entry);
        var db = e.Entry!.Value;
        Assert.Empty(db.Key.ToArray());
        Assert.Empty(db.Value.ToArray());
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
        var corruptPath = Path.Combine(_dir, $"{2ul:D20}.wal");
        await File.WriteAllBytesAsync(corruptPath, new byte[] { 0xFF }); // unknown record type

        // Assert
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        // reader should return records from first segment only and stop when hitting corrupt second
        Assert.NotEmpty(records);
        Assert.All(records, r => Assert.True(r.TxnId == 1ul || r.Entry is not null && r.Entry.Value.Sequence == 1ul));

        // ensure it did not include any records from a hypothetical second segment
        Assert.Single(records.Select(r => r.TxnId).Distinct().Where(x => x == 1ul));
    }
}
