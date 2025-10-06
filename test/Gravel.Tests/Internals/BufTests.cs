using System;
using System.Linq;
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
        Assert.Throws<ArgumentOutOfRangeException>(a1);
    }

    [Fact]
    public void should_throw_given_negative_length_when_rent_async()
    {
        // Arrange
        // Act
        Action a2 = () => Buf.AsyncRent(-1);
        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(a2);
    }

    [Fact]
    public void should_expose_empty_span_and_null_buffer_given_zero_length_when_buffer_scope()
    {
        // Arrange
        var scope = Buf.Rent(0);
        // Act / Assert
        Assert.Equal(0, scope.Span.Length);
        Assert.Null(scope.Buffer);
        scope.Dispose(); // no-op
    }

    [Fact]
    public void should_expose_empty_memory_and_span_given_zero_length_when_array_pool_scope()
    {
        // Arrange
        using var scope = Buf.AsyncRent(0);
        // Act / Assert
        Assert.Equal(0, scope.Memory.Length);
        Assert.Equal(0, scope.Span.Length);
        // Buffer returns an empty array for callers
        Assert.NotNull(scope.Buffer);
        Assert.Equal(0, scope.Buffer.Length);
    }

    [Fact]
    public void should_zero_buffer_given_clear_on_dispose_true_when_buffer_scope_disposed()
    {
        // Arrange
        var len = 32;
        var scope = Buf.Rent(len, true);
        Assert.NotNull(scope.Buffer);
        scope.Span.Fill(0x7F);
        var arr = scope.Buffer!; // capture reference
        Assert.Equal(0x7F, arr[0]);

        // Act
        scope.Dispose();

        // Assert
        Assert.Equal(0, arr[0]);
        Assert.All(arr.Take(len), b => Assert.Equal(0, b));
    }

    [Fact]
    public void should_zero_buffer_given_clear_on_dispose_true_when_array_pool_scope_disposed()
    {
        // Arrange
        var len = 24;
        using var scope = Buf.AsyncRent(len, true);
        var arr = scope.Buffer;
        Assert.NotNull(arr);
        scope.Span.Fill(0x3C);
        Assert.Equal(0x3C, arr![0]);

        // Act
        scope.Dispose();

        // Assert
        Assert.All(arr.Take(len), b => Assert.Equal(0, b));
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
        Assert.Equal(0x5A, arr[0]);
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
            Assert.Equal(10, s.Length);
            s[0] = 0x11;
            Assert.Equal(0x11, scope.Span[0]);
        }
        finally
        {
            scope.Dispose();
        }
    }
}
