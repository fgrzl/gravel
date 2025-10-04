using FluentAssertions;
using Gravel.Internals;
using Xunit;

namespace Gravel.Abstractions;

public class LexKeyTests
{
    [Fact]
    public void should_to_hex_and_from_hex_roundtrip_given_bytes_when_converted()
    {
        // Arrange
        var bytes = new byte[] { 0x01, 0xAB, 0x7F, 0x00 };
        var key = new LexKey(bytes);

        // Act
        var hex = key.ToHexString();
        var decoded = LexKey.FromHexString(hex);

        // Assert
        decoded.Bytes.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public void should_preserve_lexicographic_order_given_numeric_parts_when_encoded()
    {
        // Arrange
        var a = LexKey.Encode(1);
        var b = LexKey.Encode(2);

        // Act
        var cmp = ByteComparer.Compare(a.Bytes, b.Bytes);

        // Assert
        cmp.Should().BeNegative();
    }

    [Fact]
    public void should_report_is_empty_given_empty_lexkey()
    {
        // Arrange
        var empty = LexKey.Empty;

        // Act & Assert
        empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void should_include_separator_given_encode_first()
    {
        // Arrange
        var first = LexKey.EncodeFirst("a");

        // Act
        var firstHex = first.ToHexString();

        // Assert
        firstHex.Should().Contain("00"); // separator present
    }

    [Fact]
    public void should_end_with_end_marker_given_encode_last()
    {
        // Arrange
        var last = LexKey.EncodeLast("a");

        // Act
        var lastHex = last.ToHexString();

        // Assert
        // Expect uppercase hex produced by Convert.ToHexString
        lastHex.Should().EndWith(LexKey.EndMarker.ToString("X2"));
    }
}
