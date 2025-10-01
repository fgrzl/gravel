using System;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Gravel.Internals.Filters;

public class BloomFilterTests
{
    [Fact]
    public void should_report_is_empty_given_new_filter_when_created()
    {
        // Arrange
        var bf = BloomFilter.Create(1024);

        // Act
        var any = bf.MightContain("nope"u8);

        // Assert
        any.Should().BeFalse();
    }

    [Fact]
    public void should_report_positive_for_added_item_given_item_when_tested()
    {
        // Arrange
        var bf = BloomFilter.Create(1024);
        var data = "hello"u8.ToArray();

        // Act
        bf.Add(data);
        var res = bf.MightContain(data);

        // Assert
        res.Should().BeTrue();
    }

    [Fact]
    public void should_have_bit_array_length_matching_bits_property_when_getbits()
    {
        // Arrange
        var bf = BloomFilter.Create(1000);
        bf.Add("a"u8);

        // Act
        // Use reflection to call internal GetBits()
        var mi = typeof(BloomFilter).GetMethod("GetBits",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var rom = (ReadOnlyMemory<byte>)mi.Invoke(bf, null)!;
        var length = rom.Length;

        // Assert
        length.Should().Be((bf.Bits + 7) / 8);
    }

    [Fact]
    public void should_create_valid_filter_given_expected_items_zero_when_create()
    {
        // Arrange & Act
        var bf = BloomFilter.Create(0);

        // Assert
        bf.Bits.Should().BeGreaterOrEqualTo(8);
        bf.HashFunctions.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void should_handle_tiny_false_positive_rate_given_small_rate_when_create()
    {
        // Arrange & Act
        var bf = BloomFilter.Create(1000, 1e-9);

        // Assert
        bf.Bits.Should().BeGreaterThan(0);
        bf.HashFunctions.Should().BeGreaterThan(0);
    }

    [Fact]
    public void should_not_false_negative_given_added_item_when_tested()
    {
        // Arrange
        var bf = BloomFilter.Create(500);
        var data = "unique-item"u8.ToArray();

        // Act
        bf.Add(data);
        var result = bf.MightContain(data);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void should_handle_large_input_when_adding_and_not_throw()
    {
        // Arrange
        var bf = BloomFilter.Create(1000);
        var large = new byte[10_000];
        new Random(123).NextBytes(large);

        // Act
        var act = () => bf.Add(large);

        // Assert
        act.Should().NotThrow();
        // Optionally item likely present
        bf.MightContain(large).Should().BeTrue();
    }
}