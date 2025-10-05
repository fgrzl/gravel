using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Gravel.Internals;
using Xunit;

namespace Gravel.Storage.Shared.Tests;

public class SimpleBlockBuilderTests
{
    static BlockHandle H(ulong off, ulong size)
    {
        return new BlockHandle(off, size);
    }

    static (byte[] Key, BlockHandle Handle)[] Decode(byte[] buf)
    {
        var list = new List<(byte[] Key, BlockHandle Handle)>();
        var pos = 0;
        while (pos < buf.Length)
        {
            var keyLen = (int)VarInt.Read32(buf, ref pos);
            var key = new byte[keyLen];
            Array.Copy(buf, pos, key, 0, keyLen);
            pos += keyLen;

            var hLen = (int)VarInt.Read32(buf, ref pos);
            var hSpan = new ReadOnlySpan<byte>(buf, pos, hLen);
            var tmp = hSpan;
            var off = VarInt.Read64(ref tmp);
            var size = VarInt.Read64(ref tmp);
            pos += hLen;

            list.Add((key, new BlockHandle(off, size)));
        }

        return list.ToArray();
    }

    [Fact]
    public void should_encode_key_and_handle_pairs_in_sequence()
    {
        // Arrange
        var b = new SimpleBlockBuilder();
        b.Add("a"u8, H(10, 3));
        b.Add("b"u8, H(20, 4));

        // Act
        var bytes = b.Finish();
        var decoded = Decode(bytes);

        // Assert
        decoded.Select(d => Encoding.UTF8.GetString(d.Key)).Should().Equal("a", "b");
        decoded.Select(d => (d.Handle.Offset, d.Handle.Size)).Should().Equal((10UL, 3UL), (20UL, 4UL));
    }
}
