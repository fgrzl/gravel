using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Gravel.Engine;

public class SkipListTests
{
    [Fact]
    public void should_return_inserted_value_given_single_insert_when_tryget()
    {
        // Arrange
        var sl = new SkipList<int, string>();

        // Act
        sl.InsertOrUpdate(42, "forty-two");

        // Assert
        sl.Count.Should().Be(1);
        var ok = sl.TryGet(42, out var value);
        ok.Should().BeTrue();
        value.Should().Be("forty-two");
    }

    [Fact]
    public void should_update_value_and_not_change_count_given_existing_key_when_insertorupdate()
    {
        // Arrange
        var sl = new SkipList<int, string>();

        // Act
        sl.InsertOrUpdate(1, "one");
        sl.InsertOrUpdate(1, "ONE");

        // Assert
        sl.Count.Should().Be(1, "updating an existing key should not increment count");
        sl.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("ONE");
    }

    [Fact]
    public void should_remove_key_and_return_true_given_existing_key_when_delete()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        sl.InsertOrUpdate(5, "five");
        sl.Count.Should().Be(1);

        // Act
        var deleted = sl.Delete(5);

        // Assert
        deleted.Should().BeTrue();
        sl.Count.Should().Be(0);
        sl.TryGet(5, out _).Should().BeFalse();
    }

    [Fact]
    public void should_return_false_and_not_change_count_given_nonexistent_key_when_delete()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        sl.InsertOrUpdate(2, "two");

        // Act
        var deleted = sl.Delete(3);

        // Assert
        deleted.Should().BeFalse();
        sl.Count.Should().Be(1);
    }

    [Fact]
    public void should_return_all_in_order_given_unordered_inserts_when_scan_without_bounds()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        var rnd = new Random(123);
        var keys = Enumerable.Range(1, 20).OrderBy(_ => rnd.Next()).ToArray();
        foreach (var k in keys)
            sl.InsertOrUpdate(k, $"v{k}");

        // Act
        var scanned = sl.Scan().ToList();

        // Assert
        scanned.Select(x => x.Key).Should().BeInAscendingOrder();
        scanned.Count.Should().Be(20);
        for (var i = 1; i <= 20; i++)
        {
            scanned[i - 1].Key.Should().Be(i);
            scanned[i - 1].Value.Should().Be($"v{i}");
        }
    }

    [Fact]
    public void should_return_keys_3_to_6_given_start_3_end_7_when_scan()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        foreach (var k in Enumerable.Range(1, 10))
            sl.InsertOrUpdate(k, $"v{k}");

        // Act
        var results = sl.Scan(3, 7, true, true).Select(x => x.Key).ToList();

        // Assert
        results.Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void should_return_from_start_inclusive_given_start_only_when_scan()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        foreach (var k in Enumerable.Range(1, 5))
            sl.InsertOrUpdate(k, $"v{k}");

        // Act
        var results = sl.Scan(4, hasStart: true).Select(x => x.Key).ToList();

        // Assert
        results.Should().Equal(4, 5);
    }

    [Fact]
    public void should_return_up_to_end_exclusive_given_end_only_when_scan()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        foreach (var k in Enumerable.Range(1, 5))
            sl.InsertOrUpdate(k, $"v{k}");

        // Act
        var results = sl.Scan(end: 3, hasEnd: true).Select(x => x.Key).ToList();

        // Assert
        results.Should().Equal(1, 2);
    }

    [Fact]
    public void should_return_false_and_default_given_nonexistent_key_when_tryget()
    {
        // Arrange
        var sl = new SkipList<int, string>();

        // Act
        var found = sl.TryGet(999, out var value);

        // Assert
        found.Should().BeFalse();
        value.Should().BeNull();
    }
}