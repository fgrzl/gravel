using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Gravel.Compression.Snappy;

public class SnappyCodecTests
{
    [Fact]
    public void should_roundtrip_empty_input()
    {
        // Arrange
        var src = Array.Empty<byte>();

        // Act
        var comp = SnappyCodec.Compress(src);
        var decomp = SnappyCodec.Decompress(comp);

        // Assert
        decomp.Should().BeEquivalentTo(src);
    }

    [Fact]
    public void should_roundtrip_small_random_input()
    {
        // Arrange
        var rnd = new Random(42);
        var src = new byte[1024];
        rnd.NextBytes(src);

        // Act
        var comp = SnappyCodec.Compress(src);
        var decomp = SnappyCodec.Decompress(comp);

        // Assert
        decomp.Should().BeEquivalentTo(src);
    }

    [Fact]
    public void should_compress_repetitive_data_well()
    {
        // Arrange
        var src = Encoding.ASCII.GetBytes(new string('A', 10000));

        // Act
        var comp = SnappyCodec.Compress(src);

        // Assert size
        comp.Length.Should().BeLessThan(src.Length + 100); // allow small overhead

        // Assert correctness
        var decomp = SnappyCodec.Decompress(comp);
        decomp.Should().BeEquivalentTo(src);
    }

    [Fact]
    public void should_roundtrip_via_snappy_codec_stream()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog".PadRight(1000, 'x'));

        byte[] written;
        using (var ms = new MemoryStream())
        {
            using (var s = new SnappyCodec.SnappyStream(ms, CompressionMode.Compress, true))
            {
                s.Write(data, 0, data.Length);
                s.Flush();
            }

            written = ms.ToArray();
        }

        // Act - read via stream
        using var inMs = new MemoryStream(written);
        using var outMs = new MemoryStream();
        using (var ds = new SnappyCodec.SnappyStream(inMs, CompressionMode.Decompress, true))
        {
            var buffer = new byte[4096];
            int read;
            while ((read = ds.Read(buffer, 0, buffer.Length)) > 0)
                outMs.Write(buffer, 0, read);
        }

        // Assert
        outMs.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public void should_throw_invalid_data_given_malformed_varint_when_decompress()
    {
        // Arrange: single continuation byte -> malformed varint
        var bad = new byte[] { 0x80 };

        // Act
        Action act = () => SnappyCodec.Decompress(bad);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void should_throw_invalid_data_given_literal_exceeds_expected_length_when_decompress()
    {
        // Arrange: expected=5, but literal tries to write 6
        var buf = new byte[1 + 1 + 6];
        var pos = 0;
        // varint 5
        buf[pos++] = 0x05;
        // tag literal length 6 => stored as (len-1)<<2 | 0 = (5<<2)|0
        buf[pos++] = 5 << 2 | 0;
        // 6 bytes of data
        for (var i = 0; i < 6; i++) buf[pos++] = (byte)i;

        // Act
        Action act = () => SnappyCodec.Decompress(buf);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void should_throw_invalid_data_given_copy1_offset_zero_when_decompress()
    {
        // Arrange: expected 10, literal of 4 then COPY_1 with offset 0 -> invalid
        var buf = new byte[0];
        using (var ms = new MemoryStream())
        {
            // varint 10
            ms.WriteByte(0x0A);
            // literal len 4 (len-1=3 -> (3<<2)|0)
            ms.WriteByte(3 << 2 | 0);
            ms.Write([1, 2, 3, 4]);
            // COPY_1: kind=1, len=4 -> (0<<2)|1
            ms.WriteByte(0 << 2 | 1);
            // offset low byte = 0 -> invalid
            ms.WriteByte(0x00);
            buf = ms.ToArray();
        }

        // Act
        Action act = () => SnappyCodec.Decompress(buf);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void should_throw_invalid_data_given_copy_out_of_bounds_when_decompress()
    {
        // Arrange: expected 10, literal len 2, then COPY_2 with offset greater than written
        var buf = new byte[0];
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(0x0A); // expected 10
            ms.WriteByte(1 << 2 | 0); // literal len 2
            ms.Write([9, 9]);
            // COPY_2 tag with len=1 (encoded len-1=0)
            ms.WriteByte(0 << 2 | 2);
            // offset 0x0100 -> 256, out of bounds when w=2
            ms.Write([0x00, 0x01]);
            buf = ms.ToArray();
        }

        // Act
        Action act = () => SnappyCodec.Decompress(buf);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void should_roundtrip_large_random_input_given_one_megabyte()
    {
        // Arrange
        var rnd = new Random(7);
        var src = new byte[1_000_000];
        rnd.NextBytes(src);

        // Act
        var comp = SnappyCodec.Compress(src);
        var decomp = SnappyCodec.Decompress(comp);

        // Assert
        decomp.Should().BeEquivalentTo(src);
    }
}
