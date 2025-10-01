using System;
using FluentAssertions;
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
        res.Should().BeNegative();
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
        res.Should().Be(0);
    }

    [Fact]
    public void should_handle_nulls_and_equal_cases()
    {
        // Arrange
        byte[]? a = null;
        byte[]? b = null;
        var c = new byte[] { 1 };

        // Act & Assert
        ByteComparer.Compare(a, b).Should().Be(0);
        ByteComparer.Compare(a, c).Should().BeNegative();
        ByteComparer.Compare(c, a).Should().BePositive();
    }
}