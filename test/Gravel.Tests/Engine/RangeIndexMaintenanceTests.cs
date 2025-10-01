using System;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Gravel.Engine;

public class RangeIndexMaintenanceTests
{
    static ReadOnlyMemory<byte> B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void should_remove_ranges_below_min_sequence_when_compact_by_sequence()
    {
        var mt = new MemTable();
        mt.PutRangeTombstone(B("a").Span, B("b").Span, 1UL);
        mt.PutRangeTombstone(B("c").Span, B("d").Span, 2UL);
        mt.PutRangeTombstone(B("e").Span, B("f").Span, 3UL);

        var removed = mt.CompactRangesBySequence(3UL);
        removed.Should().Be(2);

        mt.TryGetCoveringRange(B("a").Span, out var s1).Should().BeFalse();
        mt.TryGetCoveringRange(B("c").Span, out var s2).Should().BeFalse();
        mt.TryGetCoveringRange(B("e").Span, out var s3).Should().BeTrue();
        s3.Should().Be(3UL);
    }

    [Fact]
    public void should_remove_ranges_matching_predicate_when_remove_where()
    {
        var mt = new MemTable();
        mt.PutRangeTombstone(B("a").Span, B("b").Span, 10UL);
        mt.PutRangeTombstone(B("c").Span, B("d").Span, 20UL);
        mt.PutRangeTombstone(B("e").Span, B("f").Span, 30UL);

        // remove seq < 25, and specifically remove the 'e' range
        var removed = mt.RemoveRangesWhere((s,e,seq) => seq < 25UL || s[0] == (byte)'e');
        // two removed by seq (<25) and one by key ('e') => 3
        removed.Should().Be(3);

        mt.TryGetCoveringRange(B("c").Span, out var s1).Should().BeFalse();
        mt.TryGetCoveringRange(B("e").Span, out var s2).Should().BeFalse();
    }
}
