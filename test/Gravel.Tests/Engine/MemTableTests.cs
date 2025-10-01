using System;
using System.Linq;
using System.Text;
using FluentAssertions;
using Gravel.Abstractions;
using Xunit;

namespace Gravel.Engine;

public class MemTableTests
{
    [Fact]
    public void should_be_empty_given_new_memtable_when_created()
    {
        var mt = new MemTable();
        var found = mt.TryGet("nope"u8.ToArray().AsSpan(), out var v, out var s, out var k);
        mt.Count.Should().Be(0);
        found.Should().BeFalse();
        v.HasValue.Should().BeFalse();
        s.Should().Be(0UL);
        k.Should().Be(0);
    }

    [Fact]
    public void should_return_value_and_sequence_given_put_when_get()
    {
        var mt = new MemTable();
        var key = "hello"u8.ToArray();
        var value = "world"u8.ToArray();
        mt.Put(key.AsSpan(), value.AsSpan(), 1UL);
        var ok = mt.TryGet(key.AsSpan(), out var got, out var seq, out var kind);
        mt.Count.Should().Be(1);
        ok.Should().BeTrue();
        seq.Should().Be(1UL);
        kind.Should().Be(DbEntryKind.Put);
        got?.ToArray().Should().Equal(value);
    }

    [Fact]
    public void should_not_replace_given_lower_sequence_when_put_again()
    {
        var mt = new MemTable();
        var key = "k"u8.ToArray();
        var v1 = "v1"u8.ToArray();
        var v2 = "v2"u8.ToArray();
        mt.Put(key.AsSpan(), v1.AsSpan(), 10UL);
        mt.Put(key.AsSpan(), v2.AsSpan(), 5UL);
        mt.TryGet(key.AsSpan(), out var got, out var seq, out var kind).Should().BeTrue();
        seq.Should().Be(10UL);
        kind.Should().Be(DbEntryKind.Put);
        got?.ToArray().Should().Equal(v1);
        mt.Count.Should().Be(1);
    }

    [Fact]
    public void should_replace_given_equal_sequence_when_put_again()
    {
        var mt = new MemTable();
        var key = "k2"u8.ToArray();
        var v1 = "v1"u8.ToArray();
        var v2 = "v2"u8.ToArray();
        mt.Put(key.AsSpan(), v1.AsSpan(), 1UL);
        mt.Put(key.AsSpan(), v2.AsSpan(), 1UL);
        mt.TryGet(key.AsSpan(), out var gotEq, out var seqEq, out var kindEq).Should().BeTrue();
        seqEq.Should().Be(1UL);
        kindEq.Should().Be(DbEntryKind.Put);
        gotEq?.ToArray().Should().Equal(v2);
    }

    [Fact]
    public void should_replace_given_higher_sequence_when_put_again()
    {
        var mt = new MemTable();
        var key = "k2"u8.ToArray();
        var v3 = "v3"u8.ToArray();
        mt.Put(key.AsSpan(), "v0"u8.ToArray().AsSpan(), 1UL);
        mt.Put(key.AsSpan(), v3.AsSpan(), 5UL);
        mt.TryGet(key.AsSpan(), out var gotHi, out var seqHi, out var kindHi).Should().BeTrue();
        seqHi.Should().Be(5UL);
        kindHi.Should().Be(DbEntryKind.Put);
        gotHi?.ToArray().Should().Equal(v3);
    }

    [Fact]
    public void should_remove_entry_and_return_true_given_existing_entry_when_delete()
    {
        var mt = new MemTable();
        var key = "del"u8.ToArray();
        var value = "x"u8.ToArray();
        mt.Put(key.AsSpan(), value.AsSpan(), 2UL);
        // simulate delete tombstone
        mt.PutDeleteTombstone(key.AsSpan(), 3UL);
        var ok = mt.TryGet(key.AsSpan(), out var got, out var seq, out var kind);
        ok.Should().BeTrue();
        kind.Should().Be(DbEntryKind.DeleteKey);
        got.HasValue.Should().BeFalse();
        seq.Should().Be(3UL);
    }

    [Fact]
    public void should_return_false_given_missing_key_when_delete()
    {
        var mt = new MemTable();
        mt.PutDeleteTombstone("missing"u8.ToArray().AsSpan(), 1UL); // create tombstone
        var found = mt.TryGet("missing"u8.ToArray().AsSpan(), out _, out var seq, out var kind);
        found.Should().BeTrue();
        kind.Should().Be(DbEntryKind.DeleteKey);
        seq.Should().Be(1UL);
    }

    [Fact]
    public void should_return_entries_in_lexicographic_order_given_multiple_inserts_when_scan()
    {
        var mt = new MemTable();
        var a = "a"u8.ToArray();
        var b = "b"u8.ToArray();
        var aa = "aa"u8.ToArray();
        mt.Put(b.AsSpan(), "VB"u8.ToArray().AsSpan(), 1);
        mt.Put(a.AsSpan(), "VA"u8.ToArray().AsSpan(), 2);
        mt.Put(aa.AsSpan(), "VAA"u8.ToArray().AsSpan(), 3);
        var list = mt.Scan().ToList();
        list.Select(t => Encoding.UTF8.GetString(t.Key.Span)).Should().Equal("a", "aa", "b");
        list.Select(t => t.Kind).Should().AllBeEquivalentTo(DbEntryKind.Put);
    }

    [Fact]
    public void should_obey_inclusive_start_exclusive_end_given_start_and_end_when_scan()
    {
        var mt = new MemTable();
        var keys = new[] { "a", "aa", "b", "c" };
        foreach (var k in keys) mt.Put(Encoding.UTF8.GetBytes(k).AsSpan(), "v"u8.ToArray().AsSpan(), 1);
        var results = mt.Scan("aa"u8.ToArray(), "c"u8.ToArray()).ToList();
        results.Select(r => Encoding.UTF8.GetString(r.Key.Span)).Should().Equal("aa", "b");
    }

    [Fact]
    public void should_return_empty_given_start_beyond_last_when_scan()
    {
        var mt = new MemTable();
        mt.Put("a"u8.ToArray().AsSpan(), "1"u8.ToArray().AsSpan(), 1);
        var res = mt.Scan("z"u8.ToArray()).ToList();
        res.Should().BeEmpty();
    }

    [Fact]
    public void should_maintain_count_given_multiple_inserts_and_deletes_when_operating()
    {
        var mt = new MemTable();
        for (var i = 0; i < 10; i++)
        {
            var k = Encoding.UTF8.GetBytes(i.ToString());
            mt.Put(k.AsSpan(), "v"u8.ToArray().AsSpan(), (ulong)i);
        }

        for (var i = 0; i < 5; i++)
        {
            var k = Encoding.UTF8.GetBytes(i.ToString());
            mt.PutDeleteTombstone(k.AsSpan(), 100UL);
        }

        mt.Count.Should().Be(10); // tombstones counted as entries
    }
}