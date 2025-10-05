using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Gravel.Abstractions;

/// <summary>
///     Represents an encoded composite key optimized for lexicographic sorting and range scans.
///     Provides helpers to encode heterogeneous parts into an order-preserving byte sequence.
/// </summary>
public readonly struct LexKey(byte[] bytes) : IEquatable<LexKey>, IComparer<LexKey>, IComparable<LexKey>
{
    /// <summary>
    /// Separator byte used between key parts.
    /// </summary>
    public const byte Separator = 0x00;
    /// <summary>
    /// End marker byte used for last key encoding.
    /// </summary>
    public const byte EndMarker = 0xFF;

    readonly byte[] _bytes = bytes ?? [];

    /// <summary>
    /// Gets an empty <see cref="LexKey"/> instance.
    /// </summary>
    public static readonly LexKey Empty = new([]);
    /// <summary>
    /// Gets a <see cref="LexKey"/> instance representing the last possible key.
    /// </summary>
    public static readonly LexKey Last = Encode(EndMarker);

    /// <summary>
    /// Gets the encoded bytes of the key.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;
    /// <summary>
    /// Returns true if the key is empty.
    /// </summary>
    public bool IsEmpty => _bytes.Length == 0;

    /// <summary>
    /// Returns the key as a hexadecimal string.
    /// </summary>
    /// <returns>Hexadecimal string representation of the key.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToHexString()
    {
        return _bytes.Length == 0 ? string.Empty : Convert.ToHexString(_bytes);
    }

    /// <summary>
    /// Creates a <see cref="LexKey"/> from a hexadecimal string.
    /// </summary>
    /// <param name="hex">The hex string.</param>
    /// <returns>A new <see cref="LexKey"/> instance.</returns>
    public static LexKey FromHexString(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Empty;

        var bytes = Convert.FromHexString(hex);
        return new LexKey(bytes);
    }

    /// <summary>
    /// Creates a new <see cref="LexKey"/> from heterogeneous parts.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey"/> instance.</returns>
    public static LexKey New(params object?[] parts)
    {
        if (parts.Length == 0)
            throw new ArgumentException("Cannot create LexKey: no parts provided.");

        var size = EstimateSize(parts);
        const int stackAllocThreshold = 128;
        if (size <= stackAllocThreshold)
        {
            Span<byte> stackBuf = stackalloc byte[size];
            var offset = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                var written = EncodePartToSpan(parts[i], stackBuf[offset..]);
                offset += written;
                if (i < parts.Length - 1) stackBuf[offset++] = Separator;
            }

            var result = new byte[offset];
            stackBuf[..offset].CopyTo(result);
            return new LexKey(result);
        }
        else
        {
            var buffer = new byte[size];
            var offset = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                var written = EncodePartToSpan(parts[i], buffer.AsSpan(offset));
                offset += written;
                if (i < parts.Length - 1) buffer[offset++] = Separator;
            }

            if (offset == buffer.Length)
                return new LexKey(buffer);
            var result = new byte[offset];
            buffer.AsSpan(0, offset).CopyTo(result);
            return new LexKey(result);
        }
    }

    /// <summary>
    /// Encodes heterogeneous parts into a <see cref="LexKey"/>.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey"/> instance.</returns>
    public static LexKey Encode(params object?[] parts)
    {
        return New(parts);
    }

    /// <summary>
    /// Encodes parts and appends a separator byte.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey"/> instance.</returns>
    public static LexKey EncodeFirst(params object?[] parts)
    {
        var prefix = Encode(parts);
        var dst = new byte[prefix._bytes.Length + 1];
        prefix._bytes.CopyTo(dst, 0);
        dst[prefix._bytes.Length] = Separator;
        return new LexKey(dst);
    }

    /// <summary>
    /// Encodes parts and appends an end marker byte.
    /// </summary>
    /// <param name="parts">The parts to encode.</param>
    /// <returns>A new <see cref="LexKey"/> instance.</returns>
    public static LexKey EncodeLast(params object?[] parts)
    {
        var prefix = Encode(parts);
        var dst = new byte[prefix._bytes.Length + 1];
        prefix._bytes.CopyTo(dst, 0);
        dst[prefix._bytes.Length] = EndMarker;
        return new LexKey(dst);
    }

    /// <summary>
    /// Encodes a single part into the provided span.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodePartToSpan(object? value, Span<byte> destination)
    {
        switch (value)
        {
            case null:
                destination[0] = Separator;
                return 1;
            case string s:
                return Encoding.UTF8.GetBytes(s, destination);
            case byte[] b:
                b.AsSpan().CopyTo(destination);
                return b.Length;
            case Guid g:
            {
                // Avoid allocating the intermediate string by formatting into a stack buffer
                Span<char> chars = stackalloc char[32];
                if (g.TryFormat(chars, out _, "N"))
                {
                    for (var i = 0; i < 16; i++)
                    {
                        var hi = ParseHexChar(chars[2 * i]);
                        var lo = ParseHexChar(chars[2 * i + 1]);
                        destination[i] = (byte)(hi << 4 | lo);
                    }

                    return 16;
                }

                var gb = g.ToByteArray();
                gb.AsSpan().CopyTo(destination);
                return gb.Length;
            }
            case int i:
                return EncodeInt64(i, destination);
            case long l:
                return EncodeInt64(l, destination);
            case short s16:
                return EncodeInt16(s16, destination);
            case uint ui:
                return EncodeUInt32(ui, destination);
            case ulong ul:
                return EncodeUInt64(ul, destination);
            case ushort us:
                return EncodeUInt16(us, destination);
            case byte ub:
                destination[0] = ub;
                return 1;
            case float f:
                return EncodeFloat32(f, destination);
            case double d:
                return EncodeFloat64(d, destination);
            case bool bb:
                destination[0] = (byte)(bb ? 1 : 0);
                return 1;
            case DateTime dt:
                return EncodeInt64(dt.ToUniversalTime().Ticks, destination);
            case TimeSpan ts:
                return EncodeInt64(ts.Ticks, destination);
            case LexKey k:
                k.Bytes.Span.CopyTo(destination);
                return k.Bytes.Length;
            default:
                throw new ArgumentException($"Unsupported type: {value.GetType()}");
        }
    }

    /// <summary>
    /// Parses a hexadecimal character to its byte value.
    /// </summary>
    /// <param name="c">The hex character.</param>
    /// <returns>The byte value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ParseHexChar(char c)
    {
        if (c is <= '9' and >= '0') return c - '0';
        if (c is >= 'a' and <= 'f') return c - 'a' + 10;
        if (c is >= 'A' and <= 'F') return c - 'A' + 10;
        throw new FormatException($"Invalid hex char: {c}");
    }

    /// <summary>
    /// Estimates the total size in bytes for the encoded key parts.
    /// </summary>
    /// <param name="parts">The parts to estimate.</param>
    /// <returns>The estimated size in bytes.</returns>
    static int EstimateSize(object?[] parts)
    {
        var size = 0;
        foreach (var part in parts)
        {
            size += part switch
            {
                null => 1,
                string s => Encoding.UTF8.GetByteCount(s),
                byte[] b => b.Length,
                Guid => 16,
                int or long or ulong or DateTime or TimeSpan or double => 8,
                uint => 4,
                short or ushort => 2,
                float => 4,
                byte or bool => 1,
                _ => 1
            };

            size += 1; // separator overhead (safe upper bound)
        }

        return size;
    }

    /// <summary>
    /// Encodes a signed 64-bit integer to a canonical big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeInt64(long v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, (ulong)v ^ 0x8000_0000_0000_0000);
        return 8;
    }

    /// <summary>
    /// Encodes a signed 16-bit integer to a canonical big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeInt16(short v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(v ^ 0x8000));
        return 2;
    }

    /// <summary>
    /// Encodes an unsigned 16-bit integer to a big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeUInt16(ushort v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, v);
        return 2;
    }

    /// <summary>
    /// Encodes an unsigned 32-bit integer to a big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeUInt32(uint v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, v);
        return 4;
    }

    /// <summary>
    /// Encodes an unsigned 64-bit integer to a big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeUInt64(ulong v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, v);
        return 8;
    }

    /// <summary>
    /// Encodes a 32-bit floating point value to a canonical big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeFloat32(float v, Span<byte> destination)
    {
        var bits = BitConverter.SingleToUInt32Bits(v);
        if (float.IsNaN(v)) bits = 0x7FC00001;
        else if (v < 0) bits = ~bits;
        else bits ^= 1u << 31;

        BinaryPrimitives.WriteUInt32BigEndian(destination, bits);
        return 4;
    }

    /// <summary>
    /// Encodes a 64-bit floating point value to a canonical big-endian byte array.
    /// </summary>
    /// <param name="v">The value to encode.</param>
    /// <param name="destination">The destination span.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeFloat64(double v, Span<byte> destination)
    {
        var bits = BitConverter.DoubleToUInt64Bits(v);
        if (double.IsNaN(v)) bits = 0x7FF8000000000001;
        else if (v < 0) bits = ~bits;
        else bits ^= 1UL << 63;

        BinaryPrimitives.WriteUInt64BigEndian(destination, bits);
        return 8;
    }

    /// <summary>
    /// Determines whether this key is equal to another <see cref="LexKey"/>.
    /// </summary>
    /// <param name="other">The other key.</param>
    /// <returns>True if equal, otherwise false.</returns>
    public bool Equals(LexKey other)
    {
        return SpanEquals(_bytes.AsSpan(), other._bytes.AsSpan());
    }

    /// <summary>
    /// Determines whether this key is equal to another object.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True if equal, otherwise false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is LexKey k && Equals(k);
    }

    /// <summary>
    /// Gets the hash code for this key.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return ComputeHash(_bytes.AsSpan());
    }

    /// <summary>
    /// Computes a hash for the given span using xxHash64.
    /// </summary>
    /// <param name="span">The byte span.</param>
    /// <returns>The computed hash code.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ComputeHash(ReadOnlySpan<byte> span)
    {
        // Use xxHash64.Intrinsics where available
        var h = XxHash64.HashToUInt64(span);
        return (int)(h ^ h >> 32);
    }

    /// <summary>
    /// Compares two byte spans for equality using hardware acceleration if available.
    /// </summary>
    /// <param name="a">The first span.</param>
    /// <param name="b">The second span.</param>
    /// <returns>True if equal, otherwise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool SpanEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        if (a.Length == 0) return true;

        // If hardware acceleration not available, fall back to SequenceEqual which may be optimized per runtime
        if (!Vector.IsHardwareAccelerated)
            return a.SequenceEqual(b);

        var len = a.Length;
        var vectorSize = Vector<byte>.Count;
        int i;

        var vectorCount = len / vectorSize;
        var vecA = MemoryMarshal.Cast<byte, Vector<byte>>(a[..(vectorCount * vectorSize)]);
        var vecB = MemoryMarshal.Cast<byte, Vector<byte>>(b[..(vectorCount * vectorSize)]);

        for (var vi = 0; vi < vecA.Length; vi++)
        {
            var va = vecA[vi];
            var vb = vecB[vi];
            if (va.Equals(vb))
                continue;

            var baseIndex = vi * vectorSize;
            for (var j = 0; j < vectorSize; j++)
                if (a[baseIndex + j] != b[baseIndex + j])
                    return false;
        }

        for (i = vectorCount * vectorSize; i < len; i++)
            if (a[i] != b[i])
                return false;

        return true;
    }

    /// <summary>
    /// Compares two byte spans lexicographically using hardware acceleration if available.
    /// </summary>
    /// <param name="a">The first span.</param>
    /// <param name="b">The second span.</param>
    /// <returns>Negative if a &lt; b, zero if equal, positive if a &gt; b.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int SpanCompare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var min = Math.Min(a.Length, b.Length);
        if (min == 0) return a.Length - b.Length;

        if (!Vector.IsHardwareAccelerated)
            return a.SequenceCompareTo(b);

        var vectorSize = Vector<byte>.Count;
        var vectorCount = min / vectorSize;
        var vecA = MemoryMarshal.Cast<byte, Vector<byte>>(a[..(vectorCount * vectorSize)]);
        var vecB = MemoryMarshal.Cast<byte, Vector<byte>>(b[..(vectorCount * vectorSize)]);

        for (var vi = 0; vi < vecA.Length; vi++)
        {
            var va = vecA[vi];
            var vb = vecB[vi];
            if (va.Equals(vb))
                continue;

            var baseIndex = vi * vectorSize;
            for (var j = 0; j < vectorSize; j++)
            {
                var ai = a[baseIndex + j];
                var bi = b[baseIndex + j];
                if (ai != bi)
                    return ai - bi;
            }
        }

        for (var i = vectorCount * vectorSize; i < min; i++)
        {
            var ai = a[i];
            var bi = b[i];
            if (ai != bi)
                return ai - bi;
        }

        return a.Length - b.Length;
    }

    /// <summary>
    /// Determines whether two <see cref="LexKey"/> instances are equal.
    /// </summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>True if equal, otherwise false.</returns>
    public static bool operator ==(LexKey left, LexKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two <see cref="LexKey"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>True if not equal, otherwise false.</returns>
    public static bool operator !=(LexKey left, LexKey right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Compares two <see cref="LexKey"/> instances for lexicographic order.
    /// </summary>
    /// <param name="x">The first key.</param>
    /// <param name="y">The second key.</param>
    /// <returns>-1 if x &lt; y, 0 if equal, 1 if x &gt; y.</returns>
    public int Compare(LexKey x, LexKey y)
    {
        return SpanCompare(x._bytes.AsSpan(), y._bytes.AsSpan());
    }

    /// <summary>
    /// Compares this key to another <see cref="LexKey"/> for lexicographic order.
    /// </summary>
    /// <param name="other">The other key.</param>
    /// <returns>-1 if this &lt; other, 0 if equal, 1 if this &gt; other.</returns>
    public int CompareTo(LexKey other)
    {
        return Compare(this, other);
    }
}
