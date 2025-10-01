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
    public const byte Separator = 0x00;
    public const byte EndMarker = 0xFF;

    readonly byte[] _bytes = bytes ?? [];

    public static readonly LexKey Empty = new([]);
    public static readonly LexKey Last = Encode(EndMarker);

    public ReadOnlyMemory<byte> Bytes => _bytes;
    public bool IsEmpty => _bytes.Length == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToHexString()
    {
        return _bytes.Length == 0 ? string.Empty : Convert.ToHexString(_bytes);
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

    public static LexKey Encode(params object?[] parts)
    {
        return New(parts);
    }

    public static LexKey EncodeFirst(params object?[] parts)
    {
        var prefix = Encode(parts);
        var dst = new byte[prefix._bytes.Length + 1];
        prefix._bytes.CopyTo(dst, 0);
        dst[prefix._bytes.Length] = Separator;
        return new LexKey(dst);
    }

    public static LexKey EncodeLast(params object?[] parts)
    {
        var prefix = Encode(parts);
        var dst = new byte[prefix._bytes.Length + 1];
        prefix._bytes.CopyTo(dst, 0);
        dst[prefix._bytes.Length] = EndMarker;
        return new LexKey(dst);
    }

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
                        var lo = ParseHexChar(chars[(2 * i) + 1]);
                        destination[i] = (byte)((hi << 4) | lo);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ParseHexChar(char c)
    {
        if (c is <= '9' and >= '0') return c - '0';
        if (c is >= 'a' and <= 'f') return c - 'a' + 10;
        if (c is >= 'A' and <= 'F') return c - 'A' + 10;
        throw new FormatException($"Invalid hex char: {c}");
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeInt64(long v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, (ulong)v ^ 0x8000_0000_0000_0000);
        return 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeInt16(short v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(v ^ 0x8000));
        return 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeUInt16(ushort v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, v);
        return 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeUInt32(uint v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, v);
        return 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeUInt64(ulong v, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, v);
        return 8;
    }

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

    // Equality and comparison implementations
    public bool Equals(LexKey other)
    {
        return SpanEquals(_bytes.AsSpan(), other._bytes.AsSpan());
    }

    public override bool Equals(object? obj)
    {
        return obj is LexKey k && Equals(k);
    }

    public override int GetHashCode()
    {
        return ComputeHash(_bytes.AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ComputeHash(ReadOnlySpan<byte> span)
    {
        // Use xxHash64.Intrinsics where available
        var h = XxHash64.HashToUInt64(span);
        return (int)(h ^ (h >> 32));
    }

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

    public static bool operator ==(LexKey left, LexKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LexKey left, LexKey right)
    {
        return !left.Equals(right);
    }

    public int Compare(LexKey x, LexKey y)
    {
        return SpanCompare(x._bytes.AsSpan(), y._bytes.AsSpan());
    }

    public int CompareTo(LexKey other)
    {
        return Compare(this, other);
    }
}