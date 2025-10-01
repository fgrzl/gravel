namespace Gravel.Internals;

public static class Varint
{
    public static int Write32(Span<byte> dst, uint v)
    {
        return Write64(dst, v);
    }

    public static int Write64(Span<byte> dst, ulong v)
    {
        var i = 0;
        while (v >= 0x80)
        {
            if (i >= dst.Length) throw new ArgumentException("Destination span too small to write varint", nameof(dst));
            dst[i++] = (byte)((v & 0x7FUL) | 0x80UL);
            v >>= 7;
        }

        if (i >= dst.Length) throw new ArgumentException("Destination span too small to write varint", nameof(dst));
        dst[i++] = (byte)(v & 0x7FUL);
        return i;
    }

    public static uint Read32(byte[] buf, ref int pos)
    {
        if (buf == null) throw new ArgumentNullException(nameof(buf));
        uint result = 0;
        var shift = 0;

        // varint32 max length is 5 bytes
        for (var i = 0; i < 5; i++)
        {
            if (pos >= buf.Length) throw new EndOfStreamException("Unexpected end of buffer while reading varint32");
            var b = buf[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }

        throw new FormatException("Malformed varint32");
    }

    public static ulong Read64(ref ReadOnlySpan<byte> span)
    {
        ulong result = 0;
        var shift = 0;
        var i = 0;

        // varint64 max length is 10 bytes
        for (var cnt = 0; cnt < 10; cnt++)
        {
            if (i >= span.Length) throw new EndOfStreamException("Unexpected end of span while reading varint64");
            var b = span[i++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                span = span[i..];
                return result;
            }

            shift += 7;
        }

        throw new FormatException("Malformed varint64");
    }
}