using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using FluentAssertions;
using Gravel.Storage.Shared;
using Xunit;

namespace Gravel.Storage.Shared.Tests;

public class RangeDeleteBlockBuilderTests
{
    static (byte[] Start, byte[] End, ulong Seq)[] Decode(byte[] buf)
    {
        var list = new System.Collections.Generic.List<(byte[] Start, byte[] End, ulong Seq)>();
        var pos = 0;
        while (pos < buf.Length)
        {
            var sLen = (int)Gravel.Internals.VarInt.Read32(buf, ref pos);
            var s = new byte[sLen];
            Array.Copy(buf, pos, s, 0, sLen);
            pos += sLen;

            var eLen = (int)Gravel.Internals.VarInt.Read32(buf, ref pos);
            var e = new byte[eLen];
            Array.Copy(buf, pos, e, 0, eLen);
            pos += eLen;

            var seq = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(pos, 8));
            pos += 8;

            list.Add((s, e, seq));
        }
        return list.ToArray();
    }

    [Fact]
    public void should_serialize_sorted_ranges_with_sequences()
    {
        // Arrange
        var b = new RangeDeleteBlockBuilder();
        b.Add("c"u8, "d"u8, 2);
        b.Add("a"u8, "b"u8, 1);

        // Act
        var bytes = b.Finish();
        var decoded = Decode(bytes);

        // Assert: entries sorted by start
        decoded.Select(x => Encoding.UTF8.GetString(x.Start)).Should().Equal("a", "c");
        decoded.Select(x => x.Seq).Should().Equal(1UL, 2UL);
    }

    [Fact]
    public void should_return_empty_given_no_entries()
    {
        // Arrange
        var b = new RangeDeleteBlockBuilder();

        // Act
        var bytes = b.Finish();

        // Assert
        bytes.Should().BeEmpty();
    }
}
