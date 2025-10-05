using System.Buffers.Binary;
using System.Text;

namespace Gravel;

/// <summary>
///     Represents an encoded key optimized for lexicographic sorting.
///     Provides static helpers for encoding, decoding, and comparison.
/// </summary>
public readonly struct LexKey(byte[] bytes)
{
    /// <summary>
    ///     Separator byte used for key encoding.
    /// </summary>
    public const byte Separator = 0x00;

    /// <summary>
    ///     End marker byte used for key encoding.
    /// </summary>
    public const byte EndMarker = 0xFF;

    readonly byte[] _bytes = bytes ?? [];

    /// <summary>
    ///     Gets an empty <see cref="LexKey" /> instance.
    /// </summary>
    public static readonly LexKey Empty = new([]);

    /// <summary>
    ///     Gets a <see cref="LexKey" /> instance representing the last possible key.
    /// </summary>
    public static readonly LexKey Last = Encode(EndMarker);

    /// <summary>
    ///     Gets the encoded bytes of the key.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    /// <summary>
    ///     Returns true if the key is empty.
    /// </summary>
    public bool IsEmpty => _bytes.Length == 0;

    /// <summary>
    ///     Returns the key as a lowercase hexadecimal string.
    /// </summary>
    public string ToHexString()
    {
        return _bytes.Length == 0 ? string.Empty : Convert.ToHexString(_bytes).ToLowerInvariant();
    }

    /// <summary>
    ///     Creates a <see cref="LexKey" /> from a hexadecimal string.
    /// </summary>
    /// <param name="hex">The hex string.</param>
    /// <returns>A new <see cref="LexKey" /> instance.</returns>
    public static LexKey FromHexString(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Empty;

        var bytes = Convert.FromHexString(hex);
        return new LexKey(bytes);
    }

    /// <summary>
    ///     Creates a new <see cref="LexKey" /> from heterogeneous parts.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey" /> instance.</returns>
    public static LexKey New(params object?[] parts)
    {
        if (parts.Length == 0)
            throw new ArgumentException("Cannot create LexKey: no parts provided.");

        var size = EstimateSize(parts);
        var buffer = new byte[size];
        var span = buffer.AsSpan();
        var offset = 0;

        for (var i = 0; i < parts.Length; i++)
        {
            var encoded = EncodePart(parts[i]);
            encoded.CopyTo(span[offset..]);
            offset += encoded.Length;

            if (i < parts.Length - 1) span[offset++] = Separator;
        }

        return new LexKey(buffer[..offset].ToArray());
    }

    /// <summary>
    ///     Encodes heterogeneous parts into a <see cref="LexKey" />.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey" /> instance.</returns>
    public static LexKey Encode(params object?[] parts)
    {
        return New(parts);
    }

    /// <summary>
    ///     Encodes parts and appends a separator byte.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey" /> instance.</returns>
    public static LexKey EncodeFirst(params object?[] parts)
    {
        var prefix = Encode(parts);
        var arr = new byte[prefix.Bytes.Length + 1];
        prefix.Bytes.Span.CopyTo(arr.AsSpan(0, prefix.Bytes.Length));
        arr[^1] = Separator;
        return new LexKey(arr);
    }

    /// <summary>
    ///     Encodes parts and appends an end marker byte.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey" /> instance.</returns>
    public static LexKey EncodeLast(params object?[] parts)
    {
        var prefix = Encode(parts);
        var arr = new byte[prefix.Bytes.Length + 1];
        prefix.Bytes.Span.CopyTo(arr.AsSpan(0, prefix.Bytes.Length));
        arr[^1] = EndMarker;
        return new LexKey(arr);
    }

    /// <summary>
    ///     Encodes a single part to a byte array.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The encoded bytes.</returns>
    static byte[] EncodePart(object? value)
    {
        switch (value)
        {
            case null:
                return [Separator];
            case string s:
                return Encoding.UTF8.GetBytes(s);
            case byte[] b:
                return b;
            case Guid g:
                // RFC4122 network order (hyphenless hex)
                return Convert.FromHexString(g.ToString("N"));
            case int i:
                return EncodeInt64(i);
            case long l:
                return EncodeInt64(l);
            case short s16:
                // canonical width -> int64
                return EncodeInt64(s16);
            case uint ui:
                return EncodeUInt64(ui);
            case ulong ul:
                return EncodeUInt64(ul);
            case ushort us:
                return EncodeUInt64(us);
            case byte ub:
                return [ub];
            case float f:
                // float32 uses 4-byte canonical encoding per tests/spec
                return EncodeFloat32(f);
            case double d:
                return EncodeFloat64(d);
            case bool b:
                return [(byte)(b ? 1 : 0)];
            case DateTime dt:
                // Unix nanoseconds from epoch
                var unixNanos = (dt.ToUniversalTime().Ticks - DateTime.UnixEpoch.Ticks) * 100L;
                return EncodeInt64(unixNanos);
            case TimeSpan ts:
                return EncodeInt64(ts.Ticks);
            case LexKey k:
                return k.Bytes.ToArray();
            default:
                throw new ArgumentException($"Unsupported type: {value.GetType()}");
        }
    }

    /// <summary>
    ///     Estimates the total size in bytes for the encoded key parts.
    /// </summary>
    /// <param name="parts">The parts to estimate.</param>
    /// <returns>The estimated size in bytes.</returns>
    static int EstimateSize(object?[] parts)
    {
        var size = 0;
        foreach (var part in parts)
            switch (part)
            {
                case null: size += 1; break;
                case string s: size += Encoding.UTF8.GetByteCount(s); break;
                case byte[] b: size += b.Length; break;
                case Guid: size += 16; break;
                // canonical numeric widths: signed widen to 8 bytes, float stays 4 bytes
                case int or long or short or DateTime or TimeSpan or double: size += 8; break;
                case uint or ushort or byte or ulong: size += 8; break;
                case float: size += 4; break;
                case bool: size += 1; break;
                case LexKey k: size += k.Bytes.Length; break;
                default: size += 1; break;
            }

        // separators between parts
        if (parts.Length > 1) size += parts.Length - 1;
        return size;
    }

    /// <summary>
    ///     Encodes a signed 64-bit integer to a canonical big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <returns>The encoded bytes.</returns>
    static byte[] EncodeInt64(long v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buf, (ulong)v ^ 0x8000_0000_0000_0000UL);
        return buf.ToArray();
    }

    /// <summary>
    ///     Encodes an unsigned 64-bit integer to a big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <returns>The encoded bytes.</returns>
    static byte[] EncodeUInt64(ulong v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buf, v);
        return buf.ToArray();
    }

    /// <summary>
    ///     Encodes a 32-bit floating point value to a canonical big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <returns>The encoded bytes.</returns>
    static byte[] EncodeFloat32(float v)
    {
        Span<byte> buf = stackalloc byte[4];
        var bits = BitConverter.SingleToUInt32Bits(v);
        if (float.IsNaN(v))
            bits = 0x7FC00001u;
        else if (v < 0)
            bits = ~bits;
        else
            bits ^= 1u << 31;
        BinaryPrimitives.WriteUInt32BigEndian(buf, bits);
        return buf.ToArray();
    }

    /// <summary>
    ///     Encodes a 64-bit floating point value to a canonical big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <returns>The encoded bytes.</returns>
    static byte[] EncodeFloat64(double v)
    {
        Span<byte> buf = stackalloc byte[8];
        var bits = BitConverter.DoubleToUInt64Bits(v);
        if (double.IsNaN(v))
            bits = 0x7FF8000000000001UL;
        else if (v < 0)
            bits = ~bits;
        else
            bits ^= 1UL << 63;
        BinaryPrimitives.WriteUInt64BigEndian(buf, bits);
        return buf.ToArray();
    }

    /// <summary>
    ///     Compares two <see cref="LexKey" /> instances for lexicographic order.
    /// </summary>
    /// <param name="a">The first key.</param>
    /// <param name="b">The second key.</param>
    /// <returns>-1 if a &lt; b, 0 if equal, 1 if a &gt; b.</returns>
    public int Compare(LexKey a, LexKey b)
    {
        var x = a._bytes;
        var y = b._bytes;
        var min = Math.Min(x.Length, y.Length);
        for (var i = 0; i < min; i++)
            if (x[i] != y[i])
                return x[i] < y[i] ? -1 : 1;

        if (x.Length == y.Length) return 0;
        return x.Length < y.Length ? -1 : 1;
    }

    /// <summary>
    ///     Returns true if this key equals another <see cref="LexKey" />.
    /// </summary>
    /// <param name="other">The other key.</param>
    /// <returns>True if equal, otherwise false.</returns>
    public bool Equals(LexKey other)
    {
        if (_bytes.Length != other._bytes.Length) return false;
        return _bytes.SequenceEqual(other._bytes);
    }

    /// <summary>
    ///     Returns true if this key equals another object.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if equal, otherwise false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is LexKey k && Equals(k);
    }

    /// <summary>
    ///     Gets the hash code for this key.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        // FNV-1a 32-bit
        unchecked
        {
            var hash = 2166136261u;
            foreach (var b in _bytes)
            {
                hash ^= b;
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }
}
