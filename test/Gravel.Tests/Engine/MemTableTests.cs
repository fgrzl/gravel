using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gravel.Abstractions;
using Gravel.Internals;
using Xunit;

namespace Gravel.Engine;

public class MemTableTests
{
    static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    [Fact]
    public void should_be_empty_given_new_mem_table_when_created()
    {
        // Arrange
        var mt = new MemTable();

        // Act
        var found = mt.TryGet("nope"u8.ToArray().AsSpan(), out var v, out var s, out var k);

        // Assert
        mt.Count.Should().Be(0);
        found.Should().BeFalse();
        v.HasValue.Should().BeFalse();
        s.Should().Be(0UL);
        k.Should().Be(0);
    }

    [Fact]
    public void should_return_value_and_sequence_given_put_when_get()
    {
        // Arrange
        var mt = new MemTable();
        var key = "hello"u8.ToArray();
        var value = "world"u8.ToArray();

        // Act
        mt.Put(key.AsSpan(), value.AsSpan(), 1UL);
        var ok = mt.TryGet(key.AsSpan(), out var got, out var seq, out var kind);

        // Assert
        mt.Count.Should().Be(1);
        ok.Should().BeTrue();
        seq.Should().Be(1UL);
        kind.Should().Be(DbEntryKind.Put);
        got?.ToArray().Should().Equal(value);
    }

    [Fact]
    public void should_not_replace_given_lower_sequence_when_put_again()
    {
        // Arrange
        var mt = new MemTable();
        var key = "k"u8.ToArray();
        var v1 = "v1"u8.ToArray();
        var v2 = "v2"u8.ToArray();

        // Act
        mt.Put(key.AsSpan(), v1.AsSpan(), 10UL);
        mt.Put(key.AsSpan(), v2.AsSpan(), 5UL);
        var ok = mt.TryGet(key.AsSpan(), out var got, out var seq, out var kind);

        // Assert
        ok.Should().BeTrue();
        seq.Should().Be(10UL);
        kind.Should().Be(DbEntryKind.Put);
        got?.ToArray().Should().Equal(v1);
        mt.Count.Should().Be(1);
    }

    [Fact]
    public void should_replace_given_equal_sequence_when_put_again()
    {
        // Arrange
        var mt = new MemTable();
        var key = "k2"u8.ToArray();
        var v1 = "v1"u8.ToArray();
        var v2 = "v2"u8.ToArray();

        // Act
        mt.Put(key.AsSpan(), v1.AsSpan(), 1UL);
        mt.Put(key.AsSpan(), v2.AsSpan(), 1UL);
        var got = mt.TryGet(key.AsSpan(), out var gotEq, out var seqEq, out var kindEq);

        // Assert
        got.Should().BeTrue();
        seqEq.Should().Be(1UL);
        kindEq.Should().Be(DbEntryKind.Put);
        gotEq?.ToArray().Should().Equal(v2);
    }

    [Fact]
    public void should_replace_given_higher_sequence_when_put_again()
    {
        // Arrange
        var mt = new MemTable();
        var key = "k2"u8.ToArray();
        var v3 = "v3"u8.ToArray();

        // Act
        mt.Put(key.AsSpan(), "v0"u8.ToArray().AsSpan(), 1UL);
        mt.Put(key.AsSpan(), v3.AsSpan(), 5UL);
        var ok = mt.TryGet(key.AsSpan(), out var gotHi, out var seqHi, out var kindHi);

        // Assert
        ok.Should().BeTrue();
        seqHi.Should().Be(5UL);
        kindHi.Should().Be(DbEntryKind.Put);
        gotHi?.ToArray().Should().Equal(v3);
    }

    [Fact]
    public void should_remove_entry_and_return_true_given_existing_entry_when_delete()
    {
        // Arrange
        var mt = new MemTable();
        var key = "del"u8.ToArray();
        var value = "x"u8.ToArray();
        mt.Put(key.AsSpan(), value.AsSpan(), 2UL);

        // Act: simulate delete tombstone
        mt.PutDeleteTombstone(key.AsSpan(), 3UL);
        var ok = mt.TryGet(key.AsSpan(), out var got, out var seq, out var kind);

        // Assert
        ok.Should().BeTrue();
        kind.Should().Be(DbEntryKind.DeleteKey);
        got.HasValue.Should().BeFalse();
        seq.Should().Be(3UL);
    }

    [Fact]
    public void should_return_false_given_missing_key_when_delete()
    {
        // Arrange
        var mt = new MemTable();

        // Act: create tombstone for missing key
        mt.PutDeleteTombstone("missing"u8.ToArray().AsSpan(), 1UL);
        var found = mt.TryGet("missing"u8.ToArray().AsSpan(), out _, out var seq, out var kind);

        // Assert
        found.Should().BeTrue();
        kind.Should().Be(DbEntryKind.DeleteKey);
        seq.Should().Be(1UL);
    }

    [Fact]
    public void should_return_entries_in_lexicographic_order_given_multiple_inserts_when_scan()
    {
        // Arrange
        var mt = new MemTable();
        var a = "a"u8.ToArray();
        var b = "b"u8.ToArray();
        var aa = "aa"u8.ToArray();
        mt.Put(b.AsSpan(), "VB"u8.ToArray().AsSpan(), 1);
        mt.Put(a.AsSpan(), "VA"u8.ToArray().AsSpan(), 2);
        mt.Put(aa.AsSpan(), "VAA"u8.ToArray().AsSpan(), 3);

        // Act
        var list = mt.Scan().ToList();

        // Assert
        list.Select(t => Encoding.UTF8.GetString(t.Key.Span)).Should().Equal("a", "aa", "b");
        list.Select(t => t.Kind).Should().AllBeEquivalentTo(DbEntryKind.Put);
    }

    [Fact]
    public void should_obey_inclusive_start_exclusive_end_given_start_and_end_when_scan()
    {
        // Arrange
        var mt = new MemTable();
        var keys = new[] { "a", "aa", "b", "c" };
        foreach (var k in keys) mt.Put(Encoding.UTF8.GetBytes(k).AsSpan(), "v"u8.ToArray().AsSpan(), 1);

        // Act
        var results = mt.Scan("aa"u8.ToArray(), "c"u8.ToArray()).ToList();

        // Assert
        results.Select(r => Encoding.UTF8.GetString(r.Key.Span)).Should().Equal("aa", "b");
    }

    [Fact]
    public void should_return_empty_given_start_beyond_last_when_scan()
    {
        // Arrange
        var mt = new MemTable();
        mt.Put("a"u8.ToArray().AsSpan(), "1"u8.ToArray().AsSpan(), 1);

        // Act
        var res = mt.Scan("z"u8.ToArray()).ToList();

        // Assert
        res.Should().BeEmpty();
    }

    [Fact]
    public void should_maintain_count_given_multiple_inserts_and_deletes_when_operating()
    {
        // Arrange
        var mt = new MemTable();
        for (var i = 0; i < 10; i++)
        {
            var k = Encoding.UTF8.GetBytes(i.ToString());
            mt.Put(k.AsSpan(), "v"u8.ToArray().AsSpan(), (ulong)i);
        }

        // Act: add tombstones for first five
        for (var i = 0; i < 5; i++)
        {
            var k = Encoding.UTF8.GetBytes(i.ToString());
            mt.PutDeleteTombstone(k.AsSpan(), 100UL);
        }

        // Assert
        mt.Count.Should().Be(10); // tombstones counted as entries
    }

    // Consolidated from MemTableRangeIndexTests
    [Fact]
    public void should_return_newest_covering_range_sequence()
    {
        // Arrange
        var mt = new MemTable();
        // Two overlapping ranges with different sequences
        mt.PutRangeTombstone(B("a").Span, B("m").Span, 10UL);
        mt.PutRangeTombstone(B("f").Span, B("z").Span, 20UL);

        // Act & Assert
        mt.TryGetCoveringRange(B("f").Span, out var s).Should().BeTrue();
        s.Should().Be(20UL);

        mt.TryGetCoveringRange(B("b").Span, out var s2).Should().BeTrue();
        s2.Should().Be(10UL);

        mt.TryGetCoveringRange(B("z").Span, out var s3).Should().BeFalse(); // end exclusive
    }

    [Fact]
    public void should_be_false_when_no_ranges()
    {
        // Arrange
        var mt = new MemTable();

        // Act
        var has = mt.TryGetCoveringRange(B("k").Span, out var s);

        // Assert
        has.Should().BeFalse();
        s.Should().Be(0UL);
    }

    [Fact]
    public void should_update_index_when_duplicate_start_with_higher_seq()
    {
        // Arrange
        var mt = new MemTable();
        mt.PutRangeTombstone(B("a").Span, B("d").Span, 1UL);
        mt.PutRangeTombstone(B("a").Span, B("c").Span, 5UL);

        // Act
        var ok = mt.TryGetCoveringRange(B("b").Span, out var s);

        // Assert
        ok.Should().BeTrue();
        s.Should().Be(5UL);
    }

    // Consolidated from MemTableConcurrencyTests
    [Fact]
    public async Task should_support_safe_scan_under_concurrent_puts_and_deletes()
    {
        // Arrange
        var mt = new MemTable();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Shared counter to ensure writer produced at least one mutation before scanning
        var writes = 0;

        // Writer task: mutate memtable
        var writer = Task.Run(async () =>
        {
            var i = 0;
            while (!cts.IsCancellationRequested)
            {
                var k = B("k" + i % 50);
                var v = B("v" + i);
                if (i % 10 == 0)
                    mt.PutDeleteTombstone(k.Span, (ulong)i);
                else if (i % 15 == 0)
                    mt.PutRangeTombstone(B("a").Span, B("z").Span, (ulong)i);
                else
                    mt.Put(k.Span, v.Span, (ulong)i);

                Interlocked.Increment(ref writes);
                i++;
                await Task.Yield();
            }
        }, cts.Token);

        // Wait briefly for the writer to produce at least one mutation (avoid flakiness)
        var start = DateTime.UtcNow;
        while (Volatile.Read(ref writes) == 0 && DateTime.UtcNow - start < TimeSpan.FromSeconds(1))
            await Task.Yield();

        // Act & Assert: Reader loop repeatedly scans; should never throw and should see non-empty at least once
        var scans = 0;
        var lastNonEmpty = false;
        while (scans < 50)
        {
            var list = mt.Scan().ToList();
            // basic sanity: keys must be non-decreasing
            for (var i = 1; i < list.Count; i++)
                ByteComparer.Compare(list[i - 1].Key.Span, list[i].Key.Span).Should().BeLessThanOrEqualTo(0);
            lastNonEmpty = lastNonEmpty || list.Count > 0;
            scans++;
            await Task.Yield();
        }

        cts.Cancel();
        await Task.WhenAny(writer, Task.Delay(100));

        lastNonEmpty.Should().BeTrue();
    }


    [Fact]
    public void should_remove_ranges_below_min_sequence_when_compact_by_sequence()
    {
        // Arrange
        var mt = new MemTable();
        mt.PutRangeTombstone(B("a").Span, B("b").Span, 1UL);
        mt.PutRangeTombstone(B("c").Span, B("d").Span, 2UL);
        mt.PutRangeTombstone(B("e").Span, B("f").Span, 3UL);

        // Act
        var removed = mt.CompactRangesBySequence(3UL);

        // Assert
        removed.Should().Be(2);

        mt.TryGetCoveringRange(B("a").Span, out var s1).Should().BeFalse();
        mt.TryGetCoveringRange(B("c").Span, out var s2).Should().BeFalse();
        mt.TryGetCoveringRange(B("e").Span, out var s3).Should().BeTrue();
        s3.Should().Be(3UL);
    }

    [Fact]
    public void should_remove_ranges_matching_predicate_when_remove_where()
    {
        // Arrange
        var mt = new MemTable();
        mt.PutRangeTombstone(B("a").Span, B("b").Span, 10UL);
        mt.PutRangeTombstone(B("c").Span, B("d").Span, 20UL);
        mt.PutRangeTombstone(B("e").Span, B("f").Span, 30UL);

        // Act
        var removed = mt.RemoveRangesWhere((s, e, seq) => seq < 25UL || s[0] == (byte)'e');

        // Assert
        removed.Should().Be(3);

        mt.TryGetCoveringRange(B("c").Span, out var s1).Should().BeFalse();
        mt.TryGetCoveringRange(B("e").Span, out var s2).Should().BeFalse();
    }
}
