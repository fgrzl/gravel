using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Compression;
using Gravel.Storage.FileSystem.Sst;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gravel.Engine;

public class RangeTombstoneTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static async IAsyncEnumerable<DbEntry> Entries(params DbEntry[] items)
    {
        foreach (var e in items)
        {
            await Task.Yield();
            yield return e;
        }
    }

    [Fact]
    public async Task should_mask_puts_given_newer_range_when_get_async()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), "gravel-test-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var path = Path.Combine(dir, "data.sst");
            await using (var w = new FileSstWriter(path, 2, 64, 16 * 1024, null, null))
            {
                await w.WriteAsync(Entries(
                    DbEntry.Put(B("b"), B("1"), 10),
                    DbEntry.DeleteRange(B("a"), B("z"), 11)));
            }

            using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);

            // Act
            var got = await r.GetAsync(B("b"));

            // Assert
            got.Should().BeNull();
        }
        finally
        {
            // Best-effort cleanup of temp dir
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                /* ignore cleanup errors in tests */
            }
        }
    }

    [Fact]
    public async Task should_mask_puts_given_scan_start_inside_range_when_read_all()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), "gravel-test-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "data.sst");
            await using (var w = new FileSstWriter(path, 3, 64, 16 * 1024, null, null))
            {
                await w.WriteAsync(Entries(
                    DbEntry.Put(B("b"), B("1"), 10),
                    DbEntry.DeleteRange(B("a"), B("d"), 12),
                    DbEntry.Put(B("c"), B("2"), 11)));
            }

            using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);

            // Act
            var seen = new List<(string K, string V)>();
            await foreach (var e in r.ReadAllAsync())
            {
                var k = Encoding.UTF8.GetString(e.Key.Span);
                if (string.CompareOrdinal(k, "b") >= 0 && string.CompareOrdinal(k, "e") < 0)
                    seen.Add((k, Encoding.UTF8.GetString(e.Value.Span)));
            }

            // Assert
            seen.Should().BeEmpty();
        }
        finally
        {
            // Best-effort cleanup of temp dir
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                /* ignore cleanup errors in tests */
            }
        }
    }
}