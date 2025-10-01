using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Xunit;

namespace Gravel.Engine.Compaction;

public class CompactorDeleteKeyTests
{
    static ulong _seq;
    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    static DbEntry P(string k, string v, ulong seq) => DbEntry.Put(B(k), B(v), seq);
    static DbEntry DK(string k, ulong seq) => DbEntry.DeleteKey(B(k), seq);
    static DbEntry DR(string s, string e, ulong seq) => DbEntry.DeleteRange(B(s), B(e), seq);

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
        var f2 = MakeFile(DK("k", 20));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var outList = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            outList.Add(e);

        // Assert: only delete-key remains
        outList.Should().HaveCount(1);
        outList[0].Kind.Should().Be(DbEntryKind.DeleteKey);
        Encoding.UTF8.GetString(outList[0].Key.Span).Should().Be("k");
        outList[0].Sequence.Should().Be(20);
    }

    [Fact]
    public async Task should_retain_newer_put_given_put_after_range_tombstone_when_compacting()
    {
        // Arrange: range [a,z)@10 then put x@12 (should survive)
        var f1 = MakeFile(DR("a", "z", 10));
        var f2 = MakeFile(P("x", "vx", 12));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var outList = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            outList.Add(e);

        // Assert: expect 2 entries (range tombstone + newer put)
        outList.Should().HaveCount(2);
        outList.Any(e => e.Kind == DbEntryKind.DeleteRange).Should().BeTrue();
        var put = outList.Single(e => e.Kind == DbEntryKind.Put);
        Encoding.UTF8.GetString(put.Key.Span).Should().Be("x");
        put.Sequence.Should().Be(12);
    }

    [Fact]
    public async Task should_drop_put_given_put_covered_by_newer_range_when_compacting()
    {
        // Arrange: put b@10 then range [a,c)@15 masks it
        var f1 = MakeFile(P("b", "v", 10));
        var f2 = MakeFile(DR("a", "c", 15));
        var files = new List<SstFile> { f1, f2 };

        // Act
        var outList = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            outList.Add(e);

        // Assert: only the range tombstone remains (put dropped)
        outList.Should().HaveCount(1);
        outList[0].Kind.Should().Be(DbEntryKind.DeleteRange);
    }
}
