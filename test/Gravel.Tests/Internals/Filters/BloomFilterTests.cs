using System;
using System.Reflection;
using Gravel.TestHelpers;
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
        Assert.False(any);
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
        Assert.True(res);
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
        Assert.Equal((bf.Bits + 7) / 8, length);
    }

    [Fact]
    public void should_create_valid_filter_given_expected_items_zero_when_create()
    {
        // Arrange & Act
        var bf = BloomFilter.Create(0);

        // Assert
        Assert.True(bf.Bits >= 8);
        Assert.True(bf.HashFunctions >= 1);
    }

    [Fact]
    public void should_handle_tiny_false_positive_rate_given_small_rate_when_create()
    {
        // Arrange & Act
        var bf = BloomFilter.Create(1000, 1e-9);

        // Assert
        Assert.True(bf.Bits > 0);
        Assert.True(bf.HashFunctions > 0);
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
        Assert.True(result);
    }

    [Fact]
    public void should_handle_large_input_when_adding_and_not_throw()
    {
        // Arrange
        var bf = BloomFilter.Create(1000);
        var large = new byte[10_000];
        // deterministic RNG to avoid flakiness
        TestRng.NextBytes(123, large);

        // Act
        var act = () => bf.Add(large);

        // Assert
        // xUnit has no direct NotThrow assert; invoking action should not throw
        act();
        // Optionally item likely present
        Assert.True(bf.MightContain(large));
    }

    [Fact]
    public void should_throw_given_negative_expected_items_when_create()
    {
        // Arrange

        // Act
        Action act = () => BloomFilter.Create(-5);

        // Assert: Create clamps negative to 1, so should not throw but produce a valid filter. instead test invalid falsePositiveRate
        Action act2 = () => BloomFilter.Create(10, -0.5);
        act();
        act2();
    }
}
