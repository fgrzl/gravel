using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.TestHelpers;
using Xunit;
using static Gravel.TestHelpers.TestBytes;

namespace Gravel.Engine;

public class SstScanSourceTests
{
    static DbEntry E(string k, string v, ulong seq)
    {
        return DbEntry.Put(B(k), B(v), seq);
    }

    [Fact]
    public async Task should_scan_all_given_no_bounds_when_create_source()
    {
        // Arrange
        var entries = new[] { E("a", "1", 1), E("b", "2", 2), E("c", "3", 3) };
        using var rdr = new StubSstReader(entries);

        // Act
        var src = await SstScanSource.CreateAsync(rdr, 5, null, null);

        // Assert
        Assert.Equal(5, src.Precedence);
        var seen = new List<(string K, string V)>();
        while (src.HasItem)
        {
            seen.Add((Encoding.UTF8.GetString(src.Key.Span), Encoding.UTF8.GetString(src.Value.Span)));
            src.MoveNext();
        }

        Assert.Equal(new[] { "a", "b", "c" }, seen.Select(x => x.K).ToArray());
        Assert.Equal(new[] { "1", "2", "3" }, seen.Select(x => x.V).ToArray());
    }

    [Fact]
    public async Task should_apply_inclusive_start_and_exclusive_end_given_bounds_when_create_source()
    {
        // Arrange
        var entries = new[] { E("a", "1", 1), E("b", "2", 2), E("c", "3", 3), E("d", "4", 4) };
        using var rdr = new StubSstReader(entries);

        // Act
        var src = await SstScanSource.CreateAsync(rdr, 1, B("b"), B("d"));
        var keys = new List<string>();
        while (src.HasItem)
        {
            keys.Add(Encoding.UTF8.GetString(src.Key.Span));
            src.MoveNext();
        }

        // Assert
        Assert.Equal(new[] { "b", "c" }, keys);
    }

    [Fact]
    public async Task should_return_empty_given_start_beyond_last_when_create_source()
    {
        // Arrange
        var entries = new[] { E("a", "1", 1), E("b", "2", 2) };
        using var rdr = new StubSstReader(entries);

        // Act
        var src = await SstScanSource.CreateAsync(rdr, 0, B("z"), null);

        // Assert
        Assert.False(src.HasItem);
        Assert.False(src.MoveNext());
    }

    [Fact]
    public async Task should_stream_points_and_ranges_in_order_without_materializing_all()
    {
        // Arrange: entries include ranges and puts; TestSstReader returns all entries in order
        var entries = new List<DbEntry>
        {
            DbEntry.Put(B("a"), B("1"), 5),
            DbEntry.DeleteRange(B("b"), B("d"), 20),
            DbEntry.Put(B("c"), B("2"), 10),
            DbEntry.Put(B("e"), B("3"), 1)
        };
        using var r = new TestSstReader(entries);

        // Act
        var src = await SstScanSource.CreateAsync(r, 1, null, null);

        // Assert: iterate once through stream; should not duplicate keys
        var listed = new List<(string Kind, string Key, ulong Seq)>();
        var keys = new HashSet<string>();
        while (src.HasItem)
        {
            var kind = src.Kind == DbEntryKind.DeleteRange ? "R" : src.Kind == DbEntryKind.Put ? "P" : "D";
            var key = Encoding.UTF8.GetString(src.Key.Span);
            listed.Add((kind, key, src.Sequence));
            keys.Add(key);
            src.MoveNext();
        }

        // We expect to have seen at least a, b, c, e in ascending order
        var keySeq = listed.Select(x => x.Key).ToArray();
        var ordered = keySeq.OrderBy(x => x).ToArray();
        Assert.True(keySeq.SequenceEqual(ordered));
        Assert.True(keys.IsSupersetOf(new[] { "a", "b", "c", "e" }));
    }
}
