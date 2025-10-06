using System.Linq;
using Gravel.TestHelpers;
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
        Assert.Equal(1, sl.Count);
        var ok = sl.TryGet(42, out var value);
        Assert.True(ok);
        Assert.Equal("forty-two", value);
    }

    [Fact]
    public void should_update_value_and_not_change_count_given_existing_key_when_insert_or_update()
    {
        // Arrange
        var sl = new SkipList<int, string>();

        // Act
        sl.InsertOrUpdate(1, "one");
        sl.InsertOrUpdate(1, "ONE");

        // Assert
        Assert.Equal(1, sl.Count); // updating an existing key should not increment count
        Assert.True(sl.TryGet(1, out var value));
        Assert.Equal("ONE", value);
    }

    [Fact]
    public void should_remove_key_and_return_true_given_existing_key_when_delete()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        sl.InsertOrUpdate(5, "five");
        Assert.Equal(1, sl.Count);

        // Act
        var deleted = sl.Delete(5);

        // Assert
        Assert.True(deleted);
        Assert.Equal(0, sl.Count);
        Assert.False(sl.TryGet(5, out _));
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
        Assert.False(deleted);
        Assert.Equal(1, sl.Count);
    }

    [Fact]
    public void should_return_all_in_order_given_unordered_inserts_when_scan_without_bounds()
    {
        // Arrange
        var sl = new SkipList<int, string>();
        var rnd = TestRng.Create(123);
        var keys = Enumerable.Range(1, 20).OrderBy(_ => rnd.Next()).ToArray();
        foreach (var k in keys)
            sl.InsertOrUpdate(k, $"v{k}");

        // Act
        var scanned = sl.Scan().ToList();

        // Assert
        var keySeq = scanned.Select(x => x.Key).ToArray();
        var ordered = keySeq.OrderBy(x => x).ToArray();
        Assert.True(keySeq.SequenceEqual(ordered));
        Assert.Equal(20, scanned.Count);
        for (var i = 1; i <= 20; i++)
        {
            Assert.Equal(i, scanned[i - 1].Key);
            Assert.Equal($"v{i}", scanned[i - 1].Value);
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
        Assert.Equal(new[] { 3, 4, 5, 6 }, results);
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
        Assert.Equal(new[] { 4, 5 }, results);
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
        Assert.Equal(new[] { 1, 2 }, results);
    }

    [Fact]
    public void should_return_false_and_default_given_nonexistent_key_when_tryget()
    {
        // Arrange
        var sl = new SkipList<int, string>();

        // Act
        var found = sl.TryGet(999, out var value);

        // Assert
        Assert.False(found);
        Assert.Null(value);
    }
}
