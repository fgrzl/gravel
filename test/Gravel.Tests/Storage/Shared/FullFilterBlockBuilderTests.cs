using System;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Gravel.Internals.Filters;
using Gravel.Storage.Shared;
using Xunit;

namespace Gravel.Storage.Shared.Tests;

public class FullFilterBlockBuilderTests
{
    [Fact]
    public void should_serialize_bloom_filter_header_and_bits()
    {
        // Arrange
        var b = new FullFilterBlockBuilder(expectedEntries: 10);
        b.AddKey("a"u8);
        b.AddKey("b"u8);

        // Act
        var bytes = b.Finish();

        // Assert
        bytes.Length.Should().BeGreaterThan(12);
        var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
        var k = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        var len = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        (12 + len).Should().Be(bytes.Length);
        bits.Should().BeGreaterThan(0);
        k.Should().BeGreaterThan(0);
    }

    [Fact]
    public void should_increase_might_contain_probability_after_adds()
    {
        // Arrange
        var b = new FullFilterBlockBuilder(expectedEntries: 10);
        var bytes1 = b.Finish();
        var bf1Bits = bytes1.AsSpan(12);

        // add keys and serialize again
        b.AddKey("hello"u8);
        b.AddKey("world"u8);
        var bytes2 = b.Finish();
        var bf2Bits = bytes2.AsSpan(12);

        // Assert: bit array should have changed (more bits set)
        bf2Bits.SequenceEqual(bf1Bits).Should().BeFalse();
    }
}
