using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gravel.Internals;
using Xunit;

namespace Gravel.Storage.Shared;

public class RangeDeleteBlockBuilderTests
{
    static (byte[] Start, byte[] End, ulong Seq)[] Decode(byte[] buf)
    {
        var list = new List<(byte[] Start, byte[] End, ulong Seq)>();
        var pos = 0;
        while (pos < buf.Length)
        {
            var sLen = (int)VarInt.Read32(buf, ref pos);
            var s = new byte[sLen];
            Array.Copy(buf, pos, s, 0, sLen);
            pos += sLen;

            var eLen = (int)VarInt.Read32(buf, ref pos);
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
        var starts = decoded.Select(x => Encoding.UTF8.GetString(x.Start)).ToArray();
        Assert.Equal(new[] { "a", "c" }, starts);
        var seqs = decoded.Select(x => x.Seq).ToArray();
        Assert.Equal(new[] { 1UL, 2UL }, seqs);
    }

    [Fact]
    public void should_return_empty_given_no_entries()
    {
        // Arrange
        var b = new RangeDeleteBlockBuilder();

        // Act
        var bytes = b.Finish();

        // Assert
        Assert.Empty(bytes);
    }
}
