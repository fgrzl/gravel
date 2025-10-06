using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gravel.Storage.FileSystem.Sst;

public class FileSstWriterTests : IAsyncLifetime
{
    string _dir = string.Empty;

    public Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gravel-test-sst-writer-" + Guid.NewGuid().ToString("N"));
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
        }

        return Task.CompletedTask;
    }

    static byte[] B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static async IAsyncEnumerable<DbEntry> MakeAsyncEntries(IEnumerable<(string k, string v)> items)
    {
        ulong seq = 1;
        foreach (var (k, v) in items)
        {
            await Task.Yield();
            yield return DbEntry.Put(B(k), B(v), seq++);
        }
    }

    [Fact]
    public async Task should_write_and_read_entries_in_order()
    {
        // Arrange
        var items = new List<(string k, string v)>
        {
            ("apple", "red"),
            ("banana", "yellow"),
            ("cherry", "darkred")
        };
        var path = Path.Combine(_dir, "data.sst");

        // Act
        await using (var w = new FileSstWriter(path, items.Count, 64, 16 * 1024, null, null))
        {
            await w.WriteAsync(MakeAsyncEntries(items));
        }

        // Assert
        Assert.True(File.Exists(path));

        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        foreach (var (k, v) in items)
        {
            var got = await r.GetAsync(B(k));
            Assert.NotNull(got);
            Assert.Equal(k, Encoding.UTF8.GetString(got!.Value.Key.ToArray()));
            Assert.Equal(v, Encoding.UTF8.GetString(got.Value.Value.ToArray()));
        }
    }

    [Fact]
    public async Task should_support_get_and_might_contain()
    {
        // Arrange
        var items = new List<(string k, string v)>
        {
            ("k1", "v1"),
            ("k2", "v2"),
            ("k3", "v3")
        };
        var path = Path.Combine(_dir, "data.sst");

        // Act
        await using (var w = new FileSstWriter(path, items.Count, 64, 16 * 1024, null, null))
        {
            await w.WriteAsync(MakeAsyncEntries(items));
        }

        // Assert
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);

        var got = await r.GetAsync(B("k2"));
        Assert.NotNull(got);
        Assert.Equal("k2", Encoding.UTF8.GetString(got!.Value.Key.ToArray()));
        Assert.Equal("v2", Encoding.UTF8.GetString(got.Value.Value.ToArray()));

        var missing = await r.GetAsync(B("not-there"));
        Assert.Null(missing);

        Assert.True(await r.MightContainAsync(B("k1")));
        // may be true/false for non-existing
        _ = await r.MightContainAsync(B("not-there"));
    }

    [Fact]
    public async Task should_handle_empty_key_and_value()
    {
        // Arrange
        var path = Path.Combine(_dir, "data.sst");

        // Act
        await using (var w = new FileSstWriter(path, 1, 64, 16 * 1024, null, null))
        {
            await w.WriteAsync(MakeAsyncEntries([(string.Empty, string.Empty)]));
        }

        // Assert
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        var got = await r.GetAsync(B(""));
        Assert.NotNull(got);
        Assert.Empty(got!.Value.Key.ToArray());
        Assert.Empty(got.Value.Value.ToArray());
    }

    [Fact]
    public async Task should_remove_tmp_file_on_commit_and_keep_final()
    {
        // Arrange
        var items = Enumerable.Range(0, 10).Select(i => ($"k{i}", $"v{i}"));
        var path = Path.Combine(_dir, "data.sst");

        // Act
        await using (var w = new FileSstWriter(path, 10, 64, 16 * 1024, null, null))
        {
            await w.WriteAsync(MakeAsyncEntries(items));
        }

        // Assert
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp.*"));
    }

    [Fact]
    public async Task should_write_large_values_and_read_back()
    {
        // Arrange
        var big = new string('x', 100_000);
        var path = Path.Combine(_dir, "data.sst");

        // Act
        await using (var w = new FileSstWriter(path, 1, 64, 16 * 1024, null, null))
        {
            await w.WriteAsync(MakeAsyncEntries([("big", big)]));
        }

        // Assert
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        var e = (await r.GetAsync(B("big")))!;
        Assert.Equal(big, Encoding.UTF8.GetString(e.Value.Value.ToArray()));
    }
}
