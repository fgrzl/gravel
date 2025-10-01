using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Gravel.Internals;

public class BufTests
{
    [Fact]
    public void should_throw_given_negative_length_when_rent_sync()
    {
        // Arrange
        // Act
        Action a1 = () => Buf.Rent(-1);
        // Assert
        a1.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_given_negative_length_when_rent_async()
    {
        // Arrange
        // Act
        Action a2 = () => Buf.AsyncRent(-1);
        // Assert
        a2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_expose_empty_span_and_null_buffer_given_zero_length_when_buffer_scope()
    {
        // Arrange
        var scope = Buf.Rent(0);
        // Act / Assert
        scope.Span.Length.Should().Be(0);
        scope.Buffer.Should().BeNull();
        scope.Dispose(); // no-op
    }

    [Fact]
    public void should_expose_empty_memory_and_span_given_zero_length_when_array_pool_scope()
    {
        // Arrange
        using var scope = Buf.AsyncRent(0);
        // Act / Assert
        scope.Memory.Length.Should().Be(0);
        scope.Span.Length.Should().Be(0);
        // Buffer returns an empty array for callers
        scope.Buffer.Should().NotBeNull();
        scope.Buffer.Length.Should().Be(0);
    }

    [Fact]
    public void should_zero_buffer_given_clear_on_dispose_true_when_buffer_scope_disposed()
    {
        // Arrange
        var len = 32;
        var scope = Buf.Rent(len, true);
        scope.Buffer.Should().NotBeNull();
        scope.Span.Fill(0x7F);
        var arr = scope.Buffer!; // capture reference
        arr[0].Should().Be(0x7F);

        // Act
        scope.Dispose();

        // Assert
        arr[0].Should().Be(0);
        arr.Take(len).Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void should_zero_buffer_given_clear_on_dispose_true_when_array_pool_scope_disposed()
    {
        // Arrange
        var len = 24;
        using var scope = Buf.AsyncRent(len, true);
        var arr = scope.Buffer;
        arr.Should().NotBeNull();
        scope.Span.Fill(0x3C);
        arr![0].Should().Be(0x3C);

        // Act
        scope.Dispose();

        // Assert
        arr.Take(len).Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void should_not_clear_given_default_clear_on_dispose_false_when_buffer_scope_disposed()
    {
        // Arrange
        var len = 8;
        var scope = Buf.Rent(len); // default clearOnDispose = false
        var arr = scope.Buffer!;
        scope.Span.Fill(0x5A);

        // Act
        scope.Dispose();

        // Assert
        // since default is false, array should retain the value
        arr[0].Should().Be(0x5A);
    }

    [Fact]
    public void should_be_idempotent_given_array_pool_scope_when_dispose_called_twice()
    {
        // Arrange
        var scope = Buf.AsyncRent(16);

        // Act / Assert
        scope.Dispose();
        scope.Dispose();
    }

    [Fact]
    public void should_be_idempotent_given_buffer_scope_when_dispose_called_twice()
    {
        // Arrange
        var scope = Buf.Rent(16);

        // Act / Assert
        scope.Dispose();
        scope.Dispose();
    }

    [Fact]
    public void should_support_implicit_span_conversion_given_buffer_scope_when_cast_to_span()
    {
        // Arrange
        var scope = Buf.Rent(10);
        try
        {
            // Act
            Span<byte> s = scope;
            // Assert
            s.Length.Should().Be(10);
            s[0] = 0x11;
            scope.Span[0].Should().Be(0x11);
        }
        finally
        {
            scope.Dispose();
        }
    }
}