using System;
using System.IO;
using Xunit;

namespace Gravel.Internals;

public class VarIntTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(300u)]
    [InlineData(16384u)]
    [InlineData(uint.MaxValue)]
    public void should_roundtrip_varint32_given_value(uint value)
    {
        // Arrange
        Span<byte> buf = stackalloc byte[10];

        // Act
        var written = VarInt.Write32(buf, value);
        var arr = buf[..written].ToArray();
        var pos = 0;
        var read = VarInt.Read32(arr, ref pos);

        // Assert
        Assert.Equal(value, read);
        Assert.Equal(written, pos);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(127UL)]
    [InlineData(128UL)]
    [InlineData(300UL)]
    [InlineData(16384UL)]
    [InlineData(ulong.MaxValue)]
    public void should_roundtrip_varint64_given_value(ulong value)
    {
        // Arrange
        Span<byte> buf = stackalloc byte[20];

        // Act
        var written = VarInt.Write64(buf, value);
        ReadOnlySpan<byte> span = buf[..written];
        var read = VarInt.Read64(ref span);

        // Assert
        Assert.Equal(value, read);
        Assert.Equal(0, span.Length);
    }

    [Fact]
    public void should_throw_EndOfStreamException_given_truncated_buffer_when_read32()
    {
        // Arrange
        var arr = new byte[] { 0x80 }; // continuation but truncated

        // Act
        var act = () =>
        {
            var p = 0;
            VarInt.Read32(arr, ref p);
        };

        // Assert
        Assert.Throws<EndOfStreamException>(act);
    }

    [Fact]
    public void should_throw_EndOfStreamException_given_truncated_span_when_read64()
    {
        // Arrange
        var arr = new byte[] { 0x80 };

        // Act
        var act = () =>
        {
            var s = new ReadOnlySpan<byte>(arr);
            VarInt.Read64(ref s);
        };

        // Assert
        Assert.Throws<EndOfStreamException>(act);
    }

    [Fact]
    public void should_throw_ArgumentException_given_destination_too_small_when_write64()
    {
        // Arrange

        // Act
        Action act = () => VarInt.Write64(new Span<byte>(new byte[1]), 0xdeadbeef);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void should_throw_FormatException_given_malformed_varint_when_read32()
    {
        // Arrange
        // 5 bytes with continuation -> malformed for varint32
        var arr = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 };

        // Act
        var act = () =>
        {
            var p = 0;
            VarInt.Read32(arr, ref p);
        };

        // Assert
        Assert.Throws<FormatException>(act);
    }

    [Fact]
    public void should_throw_FormatException_given_malformed_varint_when_read64()
    {
        // Arrange
        var arr = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 };

        // Act
        var act = () =>
        {
            var s = new ReadOnlySpan<byte>(arr);
            VarInt.Read64(ref s);
        };

        // Assert
        Assert.Throws<FormatException>(act);
    }
}
