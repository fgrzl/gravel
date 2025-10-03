using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gravel.Storage.FileSystem.Sst;

public class FileSstReaderTests : IAsyncLifetime
{
    string _dir = string.Empty;

    public async Task InitializeAsync()
    {
        // Arrange temp directory for test files
        _dir = Path.Combine(Path.GetTempPath(), "gravel-test-sst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch
        {
            // Best-effort cleanup of temp files; ignore IO errors
        }

        await Task.CompletedTask;
    }

    static byte[] B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    static DbEntry E(string k, string v, ulong seq)
    {
        return DbEntry.Put(B(k), B(v), seq);
    }

    static async IAsyncEnumerable<DbEntry> MakeAsyncEntries(IEnumerable<(string k, string v)> items)
    {
        ulong seq = 1;
        foreach (var (k, v) in items)
        {
            await Task.Yield();
            yield return E(k, v, seq++);
        }
    }

    protected async Task<string> CreateSstAsync(IEnumerable<(string k, string v)> items, int expectedEntries)
    {
        var path = Path.Combine(_dir, "data.sst");
        await using var w = new FileSstWriter(path, expectedEntries, 64, 16 * 1024, null, null);
        await w.WriteAsync(MakeAsyncEntries(items));
        return path;
    }

    [Fact]
    public async Task should_read_all_entries_in_order_given_get_async()
    {
        // Arrange
        var items = new[] { ("a", "1"), ("b", "2"), ("c", "3") };
        var path = await CreateSstAsync(items, items.Length);

        // Act
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);

        // Assert
        foreach (var (k, v) in items)
        {
            var got = await r.GetAsync(B(k));
            got.Should().NotBeNull();
            Encoding.UTF8.GetString(got!.Value.Key.ToArray()).Should().Be(k);
            Encoding.UTF8.GetString(got.Value.Value.ToArray()).Should().Be(v);
        }
    }

    [Fact]
    public async Task should_get_existing_and_return_null_for_missing_given_sparse_index()
    {
        // Arrange
        var items = Enumerable.Range(0, 50).Select(i => ($"k{i:D3}", $"v{i:D3}")).ToArray();
        var path = await CreateSstAsync(items, items.Length);

        // Act
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        const string target = "k027";
        var got = await r.GetAsync(B(target));

        // Assert
        got.Should().NotBeNull();
        Encoding.UTF8.GetString(got!.Value.Key.ToArray()).Should().Be(target);
        Encoding.UTF8.GetString(got.Value.Value.ToArray()).Should().Be("v027");

        var missing = await r.GetAsync(B("z999"));
        missing.Should().BeNull();
    }

    [Fact]
    public async Task should_report_might_contain_true_for_hits_and_allow_false_positive_for_misses_given_bloom()
    {
        // Arrange
        var items = new[] { ("alpha", "1"), ("beta", "2") };
        var path = await CreateSstAsync(items, items.Length);

        // Act
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);

        // Assert
        (await r.MightContainAsync(B("alpha"))).Should().BeTrue();
        var missingMaybe = await r.MightContainAsync(B("missing-key"));
        _ = missingMaybe; // acceptable both ways
    }

    [Fact]
    public async Task should_handle_empty_key_and_value_given_write_and_read()
    {
        // Arrange
        var path = await CreateSstAsync([(string.Empty, string.Empty)], 1);

        // Act
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        var got = await r.GetAsync(B(""));

        // Assert
        got.Should().NotBeNull();
        got!.Value.Key.ToArray().Should().BeEmpty();
        got.Value.Value.ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_on_invalid_footer_magic_given_corrupted_footer()
    {
        // Arrange
        var items = new[] { ("k", "v") };
        var path = await CreateSstAsync(items, items.Length);

        // Act: corrupt last byte of file (footer magic)
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.Length >= 8) bytes[^1] = 0x00;
        await File.WriteAllBytesAsync(path, bytes);

        // Assert
        var act = () => { _ = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance); };
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task should_read_large_values_given_read_all()
    {
        // Arrange
        var big = new string('x', 200_000);
        var path = await CreateSstAsync([("big", big)], 1);

        // Act
        using var r = new FileSstReader(path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        var e = await r.GetAsync(B("big"));

        // Assert
        e.Should().NotBeNull();
        Encoding.UTF8.GetString(e!.Value.Value.ToArray()).Should().Be(big);
    }
}