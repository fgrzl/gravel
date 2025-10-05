namespace Gravel.Internals;

/// <summary>
///     Provides static methods for encoding and decoding variable-length integers (varint).
///     Supports 32-bit and 64-bit unsigned integers for efficient storage and transmission.
/// </summary>
public static class VarInt
{
    /// <summary>
    ///     Writes a 32-bit unsigned integer as a varint to the destination span.
    /// </summary>
    /// <param name="dst">The destination span.</param>
    /// <param name="v">The value to write.</param>
    /// <returns>The number of bytes written.</returns>
    public static int Write32(Span<byte> dst, uint v)
    {
        return Write64(dst, v);
    }

    /// <summary>
    ///     Writes a 64-bit unsigned integer as a varint to the destination span.
    /// </summary>
    /// <param name="dst">The destination span.</param>
    /// <param name="v">The value to write.</param>
    /// <returns>The number of bytes written.</returns>
    public static int Write64(Span<byte> dst, ulong v)
    {
        var i = 0;
        while (v >= 0x80)
        {
            if (i >= dst.Length) throw new ArgumentException("Destination span too small to write varint", nameof(dst));
            dst[i++] = (byte)(v & 0x7FUL | 0x80UL);
            v >>= 7;
        }

        if (i >= dst.Length) throw new ArgumentException("Destination span too small to write varint", nameof(dst));
        dst[i++] = (byte)(v & 0x7FUL);
        return i;
    }

    /// <summary>
    ///     Reads a 32-bit unsigned integer as a varint from the buffer, updating the position.
    /// </summary>
    /// <param name="buf">The buffer to read from.</param>
    /// <param name="pos">The position in the buffer (updated).</param>
    /// <returns>The decoded 32-bit unsigned integer.</returns>
    public static uint Read32(byte[] buf, ref int pos)
    {
        ArgumentNullException.ThrowIfNull(buf, nameof(buf));
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

    /// <summary>
    ///     Reads a 32-bit unsigned integer as a varint from the span, updating the position.
    /// </summary>
    /// <param name="span">The span to read from.</param>
    /// <param name="pos">The position in the span (updated).</param>
    /// <returns>The decoded 32-bit unsigned integer.</returns>
    public static uint Read32(ReadOnlySpan<byte> span, ref int pos)
    {
        uint result = 0;
        var shift = 0;

        // varint32 max length is 5 bytes
        for (var i = 0; i < 5; i++)
        {
            if (pos >= span.Length) throw new EndOfStreamException("Unexpected end of span while reading varint32");
            var b = span[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }

        throw new FormatException("Malformed varint32");
    }

    /// <summary>
    ///     Reads a 64-bit unsigned integer as a varint from the span, updating the span reference.
    /// </summary>
    /// <param name="span">The span to read from (updated).</param>
    /// <returns>The decoded 64-bit unsigned integer.</returns>
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
