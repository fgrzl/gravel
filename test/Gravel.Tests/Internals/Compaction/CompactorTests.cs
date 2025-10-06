using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Engine;
using Gravel.TestHelpers;
using Xunit;

namespace Gravel.Internals.Compaction;

public class CompactorTests
{
    static ulong _seq;

    static DbEntry Put(string k, string v)
    {
        var key = Encoding.UTF8.GetBytes(k);
        var val = Encoding.UTF8.GetBytes(v);
        return DbEntry.Put(key, val, ++_seq);
    }

    static SstFile MakeFile(params (string k, string v)[] kvs)
    {
        var entries = kvs.Select(kv => Put(kv.k, kv.v));
        return new SstFile($"mem://{Guid.NewGuid():N}", new TestSstReader(entries), 0);
    }

    [Fact]
    public async Task should_merge_multiple_sst_files_and_emit_unique_sorted_keys()
    {
        // Arrange
        var f1 = MakeFile(("a", "va1"), ("c", "vc1"));
        var f2 = MakeFile(("b", "vb2"), ("c", "vc2"), ("d", "vd2"));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var list = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            list.Add(e);

        // Assert
        var keys = list.Select(e => Encoding.UTF8.GetString(e.Key.Span)).ToList();
        Assert.Equal(new[] { "a", "b", "c", "d" }, keys);
        Assert.Equal(1, keys.Count(k => k == "c")); // deduplicated
    }

    [Fact]
    public async Task should_return_empty_given_no_files_when_merge()
    {
        // Arrange
        var files = new List<SstFile>();

        // Act
        var list = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            list.Add(e);

        // Assert
        Assert.Empty(list);
    }

    [Fact]
    public async Task should_cancel_enumeration_given_cancellation_requested()
    {
        // Arrange
        var f = MakeFile(("x", "1"), ("y", "2"));
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = async () =>
        {
            await foreach (var _ in Compactor.MergeLevelFilesAsync(new List<SstFile> { f }, cts.Token))
            {
            }
        };

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task should_deduplicate_same_key_across_files()
    {
        // Arrange: key "k" present in both files
        var f1 = MakeFile(("k", "v1"));
        var f2 = MakeFile(("k", "v2"));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var list = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            list.Add(e);

        // Assert: only one entry with key k
        Assert.Equal(1, list.Count);
        Assert.Equal("k", Encoding.UTF8.GetString(list[0].Key.Span));
    }
}
