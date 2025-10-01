using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GravelDb.LexKey
{
    public static class LexKeyConstants
    {
        public const byte Separator = 0x00;
        public const byte EndMarker = 0xFF;
    }

    /// <summary>
    /// Represents an encoded key optimized for lexicographic sorting.
    /// </summary>
    public readonly struct LexKey
    {
        private readonly byte[] _bytes;

        public static readonly LexKey Empty = new(Array.Empty<byte>());
        public static readonly LexKey Last = Encode(LexKeyConstants.EndMarker);

        public LexKey(byte[] bytes) => _bytes = bytes ?? Array.Empty<byte>();

        public ReadOnlyMemory<byte> Bytes => _bytes;

        public bool IsEmpty => _bytes.Length == 0;

        public string ToHexString() => BitConverter.ToString(_bytes).Replace("-", "").ToLowerInvariant();

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

            int size = EstimateSize(parts);
            var buffer = new byte[size];
            var span = buffer.AsSpan();
            int offset = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                var encoded = EncodePart(parts[i]);
                encoded.CopyTo(span[offset..]);
                offset += encoded.Length;

                if (i < parts.Length - 1)
                {
                    span[offset++] = LexKeyConstants.Separator;
                }
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
            return new LexKey(prefix.Bytes.ToArray().Append(LexKeyConstants.Separator).ToArray());
        }

        public static LexKey EncodeLast(params object?[] parts)
        {
            var prefix = Encode(parts);
            return new LexKey(prefix.Bytes.ToArray().Append(LexKeyConstants.EndMarker).ToArray());
        }

        private static byte[] EncodePart(object? value)
        {
            switch (value)
            {
                case null:
                    return new[] { LexKeyConstants.Separator };
                case string s:
                    return Encoding.UTF8.GetBytes(s);
                case byte[] b:
                    return b;
                case Guid g:
                    return g.ToByteArray();
                case int i:
                    return EncodeInt64(i);
                case long l:
                    return EncodeInt64(l);
                case short s16:
                    return EncodeInt16(s16);
                case uint ui:
                    return EncodeUInt32(ui);
                case ulong ul:
                    return EncodeUInt64(ul);
                case ushort us:
                    return EncodeUInt16(us);
                case byte ub:
                    return new[] { ub };
                case float f:
                    return EncodeFloat32(f);
                case double d:
                    return EncodeFloat64(d);
                case bool b:
                    return new[] { (byte)(b ? 1 : 0) };
                case DateTime dt:
                    return EncodeInt64(dt.ToUniversalTime().Ticks);
                case TimeSpan ts:
                    return EncodeInt64(ts.Ticks);
                case LexKey k:
                    return k.Bytes.ToArray();
                default:
                    throw new ArgumentException($"Unsupported type: {value.GetType()}");
            }
        }

        private static int EstimateSize(object?[] parts)
        {
            int size = 0;
            foreach (var part in parts)
            {
                switch (part)
                {
                    case null: size += 1; break;
                    case string s: size += Encoding.UTF8.GetByteCount(s); break;
                    case byte[] b: size += b.Length; break;
                    case Guid: size += 16; break;
                    case int or long or ulong or DateTime or TimeSpan or double: size += 8; break;
                    case uint or int: size += 4; break;
                    case short or ushort or float: size += 2; break;
                    case byte or bool: size += 1; break;
                    default: size += 1; break;
                }
                size += 1; // separator overhead
            }
            return size;
        }

        private static byte[] EncodeInt64(long v)
        {
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buf, (ulong)v ^ 0x8000_0000_0000_0000);
            return buf.ToArray();
        }

        private static byte[] EncodeInt16(short v)
        {
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)(v ^ 0x8000));
            return buf.ToArray();
        }

        private static byte[] EncodeUInt16(ushort v)
        {
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, v);
            return buf.ToArray();
        }

        private static byte[] EncodeUInt32(uint v)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, v);
            return buf.ToArray();
        }

        private static byte[] EncodeUInt64(ulong v)
        {
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buf, v);
            return buf.ToArray();
        }

        private static byte[] EncodeFloat32(float v)
        {
            Span<byte> buf = stackalloc byte[4];
            uint bits = BitConverter.SingleToUInt32Bits(v);
            if (float.IsNaN(v))
                bits = 0x7FC00001;
            else if (v < 0)
                bits = ~bits;
            else
                bits ^= 1u << 31;
            BinaryPrimitives.WriteUInt32BigEndian(buf, bits);
            return buf.ToArray();
        }

        private static byte[] EncodeFloat64(double v)
        {
            Span<byte> buf = stackalloc byte[8];
            ulong bits = BitConverter.DoubleToUInt64Bits(v);
            if (double.IsNaN(v))
                bits = 0x7FF8000000000001;
            else if (v < 0)
                bits = ~bits;
            else
                bits ^= 1UL << 63;
            BinaryPrimitives.WriteUInt64BigEndian(buf, bits);
            return buf.ToArray();
        }
    }

    /// <summary>
    /// JSON converter for LexKey (hex string).
    /// </summary>
    public class LexKeyJsonConverter : JsonConverter<LexKey>
    {
        public override LexKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return LexKey.Empty;

            string hex = reader.GetString() ?? "";
            return LexKey.FromHexString(hex);
        }

        public override void Write(Utf8JsonWriter writer, LexKey value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToHexString());
        }
    }
}
