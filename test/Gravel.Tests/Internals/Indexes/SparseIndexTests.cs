using System;
using System.Linq;
using System.Text;
using Xunit;

namespace Gravel.Internals.Indexes;

public class SparseIndexTests
{
    static byte[] B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    [Fact]
    public void should_add_samples_and_iterate_in_order_given_ascending_keys_when_entries()
    {
        // Arrange
        var idx = new SparseIndex();

        // Act
        idx.AddSample(B("a"), 10);
        idx.AddSample(B("b"), 20);
        idx.AddSample(B("d"), 40);

        // Assert
        var ks = idx.Entries.Select(e => Encoding.UTF8.GetString(e.Key)).ToArray();
        var offs = idx.Entries.Select(e => e.Offset).ToArray();
        Assert.Equal(new[] { "a", "b", "d" }, ks);
        Assert.Equal(new[] { 10L, 20L, 40L }, offs);
        Assert.Equal(3, idx.Count);
    }

    [Fact]
    public void should_throw_given_descending_key_when_add_sample()
    {
        // Arrange
        var idx = new SparseIndex();
        idx.AddSample(B("m"), 1);

        // Act
        var act = () => idx.AddSample(B("a"), 2);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void should_return_floor_offset_given_exact_key_when_find_floor()
    {
        // Arrange
        var idx = new SparseIndex();
        idx.AddSample(B("aa"), 100);
        idx.AddSample(B("bb"), 200);
        idx.AddSample(B("cc"), 300);

        // Act
        var off = idx.FindFloor(B("bb"));

        // Assert
        Assert.Equal(200, off);
    }

    [Fact]
    public void should_return_floor_offset_given_between_keys_when_find_floor()
    {
        // Arrange
        var idx = new SparseIndex();
        idx.AddSample(B("k1"), 11);
        idx.AddSample(B("k3"), 33);
        idx.AddSample(B("k5"), 55);

        // Act
        var off = idx.FindFloor(B("k4"));

        // Assert
        Assert.Equal(33, off);
    }

    [Fact]
    public void should_return_zero_given_key_before_first_when_find_floor()
    {
        // Arrange
        var idx = new SparseIndex();
        idx.AddSample(B("m"), 7);

        // Act
        var off = idx.FindFloor(B("a"));

        // Assert
        Assert.Equal(0, off);
    }

    [Fact]
    public void should_return_last_offset_given_key_after_last_when_find_floor()
    {
        // Arrange
        var idx = new SparseIndex();
        idx.AddSample(B("a"), 1);
        idx.AddSample(B("b"), 2);

        // Act
        var off = idx.FindFloor(B("zz"));

        // Assert
        Assert.Equal(2, off);
    }

    [Fact]
    public void should_return_zero_given_no_samples_when_find_floor()
    {
        // Arrange
        var idx = new SparseIndex();

        // Act
        var off = idx.FindFloor(B("any"));

        // Assert
        Assert.Equal(0, off);
    }
}
