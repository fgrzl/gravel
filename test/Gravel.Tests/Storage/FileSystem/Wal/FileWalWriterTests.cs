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

public class FileWalWriterTests : IAsyncLifetime
{
    string _dir = string.Empty;

    public Task InitializeAsync()
    {
        // Arrange temp wal directory
        var td = new TempDirectory("gravel-test-wal-");
        _dir = td.Path;
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

    static byte[] B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

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
            Assert.Equal(1ul, w.LastSequence);
        }

        // Assert (on-disk replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        Assert.Equal(3, records.Count);

        Assert.Equal(WalConstants.RecordBeginTxn, records[0].Type);
        Assert.Equal(42ul, records[0].TxnId);

        Assert.Equal(WalConstants.RecordEntry, records[1].Type);
        Assert.Equal(42ul, records[1].TxnId);
        Assert.NotNull(records[1].Entry);
        Assert.Equal(1ul, records[1].Entry?.Sequence);
        Assert.Equal("key1", Encoding.UTF8.GetString(records[1].Entry?.Key.ToArray() ?? []));
        Assert.Equal("value1", Encoding.UTF8.GetString(records[1].Entry?.Value.ToArray() ?? []));

        Assert.Equal(WalConstants.RecordCommitTxn, records[2].Type);
        Assert.Equal(42ul, records[2].TxnId);
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
            Assert.Equal(2ul, w.LastSequence);
        }

        // Assert (replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        Assert.Equal(4, records.Count);

        Assert.Equal(WalConstants.RecordEntry, records[1].Type);
        Assert.Equal(100ul, records[1].TxnId);
        Assert.NotNull(records[1].Entry);
        var dbEntry = records[1].Entry!.Value;
        Assert.Equal(B("dkey"), dbEntry.Key.ToArray());
        Assert.Empty(records[1].Entry?.Value.ToArray());

        Assert.Equal(WalConstants.RecordEntry, records[2].Type);
        Assert.NotNull(records[2].Entry);
        Assert.Equal(B("rstart"), records[2].Entry!.Value.Key.ToArray());
        Assert.Equal(B("rend"), records[2].Entry?.Value.ToArray());
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
            Assert.Equal(5ul, w.LastSequence);
        }

        // Assert (files and replay)
        var files = Directory.GetFiles(_dir, "*.wal").OrderBy(f => f).ToList();
        Assert.True(files.Count > 1);

        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        Assert.Equal(5 * 3, records.Count);

        var entrySeqs = records.Where(r => r.Type == WalConstants.RecordEntry).Select(r => r.Entry!.Value.Sequence)
            .ToList();
        var seqs = entrySeqs.ToArray();
        var sorted = seqs.OrderBy(x => x).ToArray();
        Assert.True(seqs.SequenceEqual(sorted));
        Assert.Equal(5, entrySeqs.Count);
        Assert.Equal(5, entrySeqs.Distinct().Count());
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
            Assert.Equal(1ul, w.LastSequence);
        }

        // Assert (replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        Assert.Equal(3, records.Count);
        var e = records.Single(r => r.Type == WalConstants.RecordEntry);
        Assert.NotNull(e.Entry);
        Assert.Empty(e.Entry!.Value.Key.ToArray());
        Assert.Empty(e.Entry.Value.Value.ToArray());
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
            Assert.Equal(1ul, w.LastSequence);
        }

        // Assert (replay)
        await using var reader = new FileWalReader(_dir);
        var records = new List<WalRecord>();
        await foreach (var r in reader.ReplayAsync()) records.Add(r);

        // Expect begin, entry, rollback
        Assert.Equal(3, records.Count);
        Assert.Equal(WalConstants.RecordRollbackTxn, records[2].Type);
        Assert.Equal(9ul, records[2].TxnId);
    }
}
