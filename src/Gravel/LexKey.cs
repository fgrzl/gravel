using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace Gravel;

public static class LexKeyConstants
{
    public const byte Separator = 0x00;
    public const byte EndMarker = 0xFF;
}

/// <summary>
///     Represents an encoded key optimized for lexicographic sorting.
/// </summary>
public readonly struct LexKey(byte[] bytes)
{
    readonly byte[] _bytes = bytes ?? Array.Empty<byte>();

    public static readonly LexKey Empty = new(Array.Empty<byte>());
    public static readonly LexKey Last = Encode(LexKeyConstants.EndMarker);

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public bool IsEmpty => _bytes.Length == 0;

    public string ToHexString()
    {
        return BitConverter.ToString(_bytes).Replace("-", "").ToLowerInvariant();
    }

    public static LexKey FromHexString(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Empty;

        var bytes = Convert.FromHexString(hex);
        return new LexKey(bytes);
    }

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

            if (i < parts.Length - 1) span[offset++] = LexKeyConstants.Separator;
        }

        return new LexKey(buffer[..offset].ToArray());
    }

    public static LexKey Encode(params object?[] parts)
    {
        return New(parts);
    }

    public static LexKey EncodeFirst(params object?[] parts)
    {
        var prefix = Encode(parts);
        var arr = new byte[prefix.Bytes.Length + 1];
        prefix.Bytes.Span.CopyTo(arr.AsSpan(0, prefix.Bytes.Length));
        arr[^1] = LexKeyConstants.Separator;
        return new LexKey(arr);
    }

    public static LexKey EncodeLast(params object?[] parts)
    {
        var prefix = Encode(parts);
        var arr = new byte[prefix.Bytes.Length + 1];
        prefix.Bytes.Span.CopyTo(arr.AsSpan(0, prefix.Bytes.Length));
        arr[^1] = LexKeyConstants.EndMarker;
        return new LexKey(arr);
    }

    static byte[] EncodePart(object? value)
    {
        switch (value)
        {
            case null:
                return new byte[] { LexKeyConstants.Separator };
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
                return new byte[] { ub };
            case float f:
                // float32 uses 4-byte canonical encoding per tests/spec
                return EncodeFloat32(f);
            case double d:
                return EncodeFloat64(d);
            case bool b:
                return new byte[] { (byte)(b ? 1 : 0) };
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

    static int EstimateSize(object?[] parts)
    {
        var size = 0;
        foreach (var part in parts)
        {
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
        }

        // separators between parts
        if (parts.Length > 1) size += parts.Length - 1;
        return size;
    }

    static byte[] EncodeInt64(long v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buf, (ulong)v ^ 0x8000_0000_0000_0000UL);
        return buf.ToArray();
    }

    static byte[] EncodeInt16(short v)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)((ushort)v ^ 0x8000));
        return buf.ToArray();
    }

    static byte[] EncodeUInt16(ushort v)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, v);
        return buf.ToArray();
    }

    static byte[] EncodeUInt32(uint v)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, v);
        return buf.ToArray();
    }

    static byte[] EncodeUInt64(ulong v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buf, v);
        return buf.ToArray();
    }

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

    // Comparison and equality helpers
    public int Compare(LexKey a, LexKey b)
    {
        var x = a._bytes;
        var y = b._bytes;
        var min = Math.Min(x.Length, y.Length);
        for (var i = 0; i < min; i++)
        {
            if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
        }

        if (x.Length == y.Length) return 0;
        return x.Length < y.Length ? -1 : 1;
    }

    public bool Equals(LexKey other)
    {
        if (_bytes.Length != other._bytes.Length) return false;
        return _bytes.SequenceEqual(other._bytes);
    }

    public override bool Equals(object? obj)
    {
        return obj is LexKey k && Equals(k);
    }

    public override int GetHashCode()
    {
        // FNV-1a 32-bit
        unchecked
        {
            uint hash = 2166136261u;
            foreach (var b in _bytes)
            {
                hash ^= b;
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }
}

/// <summary>
///     JSON converter for LexKey (hex string).
/// </summary>
public class LexKeyJsonConverter : JsonConverter<LexKey>
{
    public override LexKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return LexKey.Empty;

        var hex = reader.GetString() ?? "";
        return LexKey.FromHexString(hex);
    }

    public override void Write(Utf8JsonWriter writer, LexKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToHexString());
    }
}