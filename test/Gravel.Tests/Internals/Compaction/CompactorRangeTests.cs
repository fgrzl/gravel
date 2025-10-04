using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Compression;
using Gravel.Engine;
using Gravel.Storage.FileSystem.Sst;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gravel.Internals.Compaction;

public class CompactorRangeTests : IAsyncLifetime
{
    string _dir = string.Empty;

    public Task InitializeAsync()
    {
        // Arrange temp directory for SST outputs
        _dir = Path.Combine(Path.GetTempPath(), "gravel-test-compactor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch
        {
            // Best-effort cleanup: test artifacts are in temp; ignore IO errors on delete
        }

        return Task.CompletedTask;
    }

    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static DbEntry P(string k, string v, ulong seq)
    {
        return DbEntry.Put(B(k), B(v), seq);
    }

    static DbEntry DK(string k, ulong seq)
    {
        return DbEntry.DeleteKey(B(k), seq);
    }

    static DbEntry DR(string s, string e, ulong seq)
    {
        return DbEntry.DeleteRange(B(s), B(e), seq);
    }

    static SstFile MakeMemFile(params DbEntry[] entries)
    {
        var rdr = new TestSstReader(entries);
        return new SstFile($"mem://{Guid.NewGuid():N}", rdr, 0);
    }

    static async Task<string> WriteMergedAsync(IEnumerable<DbEntry> merged, string path)
    {
        await using (var w = new FileSstWriter(path, merged.Count(), 64, 16 * 1024, null, NullLogger.Instance))
        {
            await w.WriteAsync(ToAsync(merged));
        }

        return path;
    }

    static async IAsyncEnumerable<DbEntry> ToAsync(IEnumerable<DbEntry> items)
    {
        foreach (var e in items)
        {
            await Task.Yield();
            yield return e;
        }
    }

    [Fact]
    public async Task should_persist_range_tombstones_and_mask_puts_given_merge_when_writing_output()
    {
        // Arrange: f1 has put b@10, f2 has range [a,c)@11 that covers b
        var f1 = MakeMemFile(P("b", "1", 10));
        var f2 = MakeMemFile(DR("a", "c", 11));
        var files = new List<SstFile> { f1, f2 };

        // Act: merge and write to a file
        var merged = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            merged.Add(e);
        var outPath = Path.Combine(_dir, "out.sst");
        await WriteMergedAsync(merged, outPath);

        // Assert: b is masked; range exists in meta and matches
        using var r = new FileSstReader(outPath, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        (await r.GetAsync(B("b"))).Should().BeNull();
        var ranges = r.GetRangeDeletes();
        ranges.Should().NotBeEmpty();
        ranges.Any(x => StringComparer.Ordinal.Compare(Encoding.UTF8.GetString(x.Start.Span), "a") == 0 &&
                        StringComparer.Ordinal.Compare(Encoding.UTF8.GetString(x.End.Span), "c") == 0 &&
                        x.Seq == 11).Should().BeTrue();
    }

    [Fact]
    public async Task should_drop_puts_given_overlapping_ranges_when_compacting()
    {
        // Arrange: f1 has [a,m)@5; f2 has [f,z)@7 and put g@6 (covered by [f,z)@7)
        var f1 = MakeMemFile(DR("a", "m", 5));
        var f2 = MakeMemFile(DR("f", "z", 7), P("g", "v", 6));
        var files = new List<SstFile> { f1, f2 };

        // Act: merge and write to a file
        var merged = new List<DbEntry>();
        await foreach (var e in Compactor.MergeLevelFilesAsync(files, CancellationToken.None))
            merged.Add(e);
        var outPath = Path.Combine(_dir, "out2.sst");
        await WriteMergedAsync(merged, outPath);

        // Assert: g is masked; at least one covering range with seq >= 7 exists
        using var r = new FileSstReader(outPath, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        (await r.GetAsync(B("g"))).Should().BeNull();
        var ranges = r.GetRangeDeletes();
        ranges.Should().NotBeEmpty();
        ranges.Any(x => StringComparer.Ordinal.Compare(Encoding.UTF8.GetString(x.Start.Span), "f") <= 0 &&
                        StringComparer.Ordinal.Compare("g", Encoding.UTF8.GetString(x.End.Span)) < 0 &&
                        x.Seq >= 7).Should().BeTrue();
    }
}