using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Gravel.Storage.Shared;

public class DataBlockBuilderTests
{
    static (int[] Restarts, int EntriesEnd) ParseRestarts(byte[] buf)
    {
        var totalLen = buf.Length;
        var restartCount = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(totalLen - 4, 4));
        var offsStart = totalLen - 4 - restartCount * 4;
        var restarts = new int[restartCount];
        for (var i = 0; i < restartCount; i++)
            restarts[i] = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offsStart + i * 4, 4));
        return (restarts, offsStart);
    }

    [Fact]
    public void should_emit_restart_array_and_count_with_valid_offsets()
    {
        // Arrange
        var b = new DataBlockBuilder(2);
        var data = new[] { ("aa", "1"), ("ab", "2"), ("ac", "3"), ("ba", "4") };
        foreach (var (k, v) in data)
            b.Add(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(v));

        // Act
        var pooled = b.FinishPooled();
        try
        {
            var buf = pooled.Buffer.AsSpan(0, pooled.Length).ToArray();
            var (restarts, entriesEnd) = ParseRestarts(buf);

            // Assert
            restarts.Should().NotBeNull();
            restarts.Length.Should().BeGreaterThan(0);
            restarts[0].Should().Be(0);
            restarts.Should().BeInAscendingOrder();
            restarts[^1].Should().BeLessThanOrEqualTo(entriesEnd);
            entriesEnd.Should().BeGreaterThan(0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled.Buffer);
        }
    }

    [Fact]
    public void should_match_current_size_to_finish_length()
    {
        // Arrange
        var b = new DataBlockBuilder(4);
        b.Add("key1"u8.ToArray(), "v1"u8.ToArray());
        b.Add("key2"u8.ToArray(), "v2"u8.ToArray());

        // Act
        var pooled = b.FinishPooled();
        try
        {
            // Assert
            pooled.Length.Should().Be(b.CurrentSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled.Buffer);
        }
    }
}
