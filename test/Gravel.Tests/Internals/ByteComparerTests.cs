using System;
using Xunit;

namespace Gravel.Internals;

public class ByteComparerTests
{
    [Fact]
    public void should_compare_byte_arrays_correctly_given_different_values()
    {
        // Arrange
        var a = new byte[] { 1, 2, 3 };
        var b = new byte[] { 1, 2, 4 };

        // Act
        var res = ByteComparer.Compare(a, b);

        // Assert
        Assert.True(res < 0);
    }

    [Fact]
    public void should_compare_spans_correctly_given_equal_values()
    {
        // Arrange
        var a = new byte[] { 1, 2 };
        var b = new byte[] { 1, 2 };

        // Act
        var res = ByteComparer.Compare(a.AsSpan(), b.AsSpan());

        // Assert
        Assert.Equal(0, res);
    }

    [Fact]
    public void should_handle_nulls_and_equal_cases()
    {
        // Arrange
        byte[]? a = null;
        byte[]? b = null;
        var c = new byte[] { 1 };

        // Act & Assert
        Assert.Equal(0, ByteComparer.Compare(a, b));
        Assert.True(ByteComparer.Compare(a, c) < 0);
        Assert.True(ByteComparer.Compare(c, a) > 0);
    }
}
