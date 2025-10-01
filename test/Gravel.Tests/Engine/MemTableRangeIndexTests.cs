using System;
using System.Linq;
using System.Text;
using FluentAssertions;
using Gravel.Abstractions;
using Xunit;

namespace Gravel.Engine;

public class MemTableRangeIndexTests
{
    static ReadOnlyMemory<byte> B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void should_return_newest_covering_range_sequence()
    {
        var mt = new MemTable();
        // Two overlapping ranges with different sequences
        mt.PutRangeTombstone(B("a").Span, B("m").Span, 10UL);
        mt.PutRangeTombstone(B("f").Span, B("z").Span, 20UL);

        mt.TryGetCoveringRange(B("f").Span, out var s).Should().BeTrue();
        s.Should().Be(20UL);

        mt.TryGetCoveringRange(B("b").Span, out var s2).Should().BeTrue();
        s2.Should().Be(10UL);

        mt.TryGetCoveringRange(B("z").Span, out var s3).Should().BeFalse(); // end exclusive
    }

    [Fact]
    public void should_be_false_when_no_ranges()
    {
        var mt = new MemTable();
        mt.TryGetCoveringRange(B("k").Span, out var s).Should().BeFalse();
        s.Should().Be(0UL);
    }

    [Fact]
    public void should_update_index_when_duplicate_start_with_higher_seq()
    {
        var mt = new MemTable();
        mt.PutRangeTombstone(B("a").Span, B("d").Span, 1UL);
        mt.PutRangeTombstone(B("a").Span, B("c").Span, 5UL);

        mt.TryGetCoveringRange(B("b").Span, out var s).Should().BeTrue();
        s.Should().Be(5UL);
    }
}
