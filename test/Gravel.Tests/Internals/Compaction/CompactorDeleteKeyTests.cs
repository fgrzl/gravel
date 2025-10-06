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

public class CompactorDeleteKeyTests
{
    static byte[] B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static DbEntry P(string k, string v, ulong seq)
    {
        return DbEntry.Put(B(k), B(v), seq);
    }

    static DbEntry Dk(string k, ulong seq)
    {
        return DbEntry.DeleteKey(B(k), seq);
    }

    static DbEntry Dr(string s, string e, ulong seq)
    {
        return DbEntry.DeleteRange(B(s), B(e), seq);
    }

    static SstFile MakeFile(params DbEntry[] entries)
    {
        var rdr = new TestSstReader(entries);
        return new SstFile($"mem://{Guid.NewGuid():N}", rdr, 0);
    }

    [Fact]
    public async Task should_emit_delete_key_and_drop_older_put_given_higher_seq_delete_when_compacting()
    {
        // Arrange: put k@10 then delete k@20
        var f1 = MakeFile(P("k", "v1", 10));
        var f2 = MakeFile(Dk("k", 20));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var outList = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            outList.Add(e);

        // Assert: only delete-key remains
        Assert.Equal(1, outList.Count);
        Assert.Equal(DbEntryKind.DeleteKey, outList[0].Kind);
        Assert.Equal("k", Encoding.UTF8.GetString(outList[0].Key.Span));
        Assert.Equal(20UL, outList[0].Sequence);
    }

    [Fact]
    public async Task should_retain_newer_put_given_put_after_range_tombstone_when_compacting()
    {
        // Arrange: range [a,z)@10 then put x@12 (should survive)
        var f1 = MakeFile(Dr("a", "z", 10));
        var f2 = MakeFile(P("x", "vx", 12));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var outList = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            outList.Add(e);

        // Assert: expect 2 entries (range tombstone + newer put)
        Assert.Equal(2, outList.Count);
        Assert.True(outList.Any(e => e.Kind == DbEntryKind.DeleteRange));
        var put = outList.Single(e => e.Kind == DbEntryKind.Put);
        Assert.Equal("x", Encoding.UTF8.GetString(put.Key.Span));
        Assert.Equal(12UL, put.Sequence);
    }

    [Fact]
    public async Task should_drop_put_given_put_covered_by_newer_range_when_compacting()
    {
        // Arrange: put b@10 then range [a,c)@15 masks it
        var f1 = MakeFile(P("b", "v", 10));
        var f2 = MakeFile(Dr("a", "c", 15));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var outList = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            outList.Add(e);

        // Assert: only the range tombstone remains (put dropped)
        Assert.Equal(1, outList.Count);
        Assert.Equal(DbEntryKind.DeleteRange, outList[0].Kind);
    }
}
