using System;
using System.Text.Json;
using Xunit;
using GravelDb.LexKey;

namespace Gravel.Tests
{
    public class LexKeyBehaviorTests
    {
        [Theory]
        [InlineData("hello", "68656c6c6f")]
        [InlineData(123, "800000000000007b")]
        [InlineData(-123, "7fffffffffffff85")]
        [InlineData(true, "01")]
        [InlineData(false, "00")]
        public void should_encode_primitives_given_part_when_encoded(object part, string expectedHex)
        {
            // Arrange

            // Act
            var key = LexKey.Encode(part);

            // Assert
            Assert.Equal(expectedHex, key.ToHexString());
        }

        [Fact]
        public void should_encode_guid_given_guid_when_encoded()
        {
            // Arrange
            var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

            // Act
            var key = LexKey.Encode(guid);

            // Assert
            Assert.Equal("550e8400e29b41d4a716446655440000", key.ToHexString());
        }

        [Fact]
        public void should_preserve_int_ordering_given_negative_zero_and_positive()
        {
            // Arrange

            // Act
            var neg = LexKey.Encode(-1);
            var zero = LexKey.Encode(0);
            var pos = LexKey.Encode(1);

            // Assert
            Assert.True(string.CompareOrdinal(neg.ToHexString(), zero.ToHexString()) < 0);
            Assert.True(string.CompareOrdinal(zero.ToHexString(), pos.ToHexString()) < 0);
        }

        [Fact]
        public void should_bracket_key_given_prefix_when_encode_first_and_last()
        {
            // Arrange

            // Act
            var key = LexKey.Encode("prefix", "a");
            var first = LexKey.EncodeFirst("prefix");
            var last = LexKey.EncodeLast("prefix");

            // Assert
            Assert.True(string.CompareOrdinal(first.ToHexString(), key.ToHexString()) <= 0);
            Assert.True(string.CompareOrdinal(last.ToHexString(), key.ToHexString()) > 0);
        }

        [Fact]
        public void should_roundtrip_json_given_lexkey_when_serialized_and_deserialized()
        {
            // Arrange
            var key = LexKey.Encode("test");
            var options = new JsonSerializerOptions { Converters = { new LexKeyJsonConverter() } };

            // Act
            var json = JsonSerializer.Serialize(key, options);
            var roundtrip = JsonSerializer.Deserialize<LexKey>(json, options);

            // Assert
            Assert.Equal(key.ToHexString(), roundtrip.ToHexString());
        }

        [Fact]
        public void should_throw_format_exception_given_invalid_hex_when_parsing()
        {
            // Arrange
            var invalid = "invalidhex";

            // Act & Assert
            Assert.Throws<FormatException>(() => LexKey.FromHexString(invalid));
        }

        [Fact]
        public void should_canonicalize_nan_given_float_and_double_when_encoded()
        {
            // Arrange

            // Act
            var f32 = LexKey.Encode(float.NaN);
            var f64 = LexKey.Encode(double.NaN);

            // Assert
            Assert.Equal("7fc00001", f32.ToHexString());
            Assert.Equal("7ff8000000000001", f64.ToHexString());
        }
    }
}
