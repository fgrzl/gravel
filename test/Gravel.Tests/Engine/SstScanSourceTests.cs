using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Xunit;

namespace Gravel.Engine;

public class SstScanSourceTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

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
        src.Precedence.Should().Be(5);
        var seen = new List<(string K, string V)>();
        while (src.HasItem)
        {
            seen.Add((Encoding.UTF8.GetString(src.Key.Span), Encoding.UTF8.GetString(src.Value.Span)));
            src.MoveNext();
        }

        seen.Select(x => x.K).Should().Equal("a", "b", "c");
        seen.Select(x => x.V).Should().Equal("1", "2", "3");
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
        keys.Should().Equal("b", "c");
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
        src.HasItem.Should().BeFalse();
        src.MoveNext().Should().BeFalse();
    }
}