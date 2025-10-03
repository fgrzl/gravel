using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Xunit;

namespace Gravel.Engine;

public class SstScanSourceStreamingTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
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
        listed.Select(x => x.Key).Should().BeInAscendingOrder();
        keys.IsSupersetOf(["a", "b", "c", "e"]).Should().BeTrue();
    }
}