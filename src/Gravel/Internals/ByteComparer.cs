using System.Runtime.CompilerServices;

namespace Gravel.Internals;

/// <summary>
///     Provides helpers for lexicographic comparison of byte sequences.
/// </summary>
public static class ByteComparer
{
    /// <summary>
    ///     Compares two byte arrays using lexicographic order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Compare(byte[]? xs, byte[]? ys)
    {
        if (ReferenceEquals(xs, ys)) return 0;
        if (xs is null) return -1;
        if (ys is null) return 1;
        return Compare(xs.AsSpan(), ys.AsSpan());
    }

    /// <summary>
    ///     Compares two byte spans using lexicographic order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Compare(ReadOnlySpan<byte> xs, ReadOnlySpan<byte> ys)
    {
        // Use the built-in SequenceCompareTo which is optimized by the runtime and may use vectorized implementations.
        var res = xs.SequenceCompareTo(ys);
        if (res != 0)
            return res;

        return res;
    }

    /// <summary>
    ///     Compares two byte memories using lexicographic order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Compare(ReadOnlyMemory<byte> xs, ReadOnlyMemory<byte> ys)
    {
        return Compare(xs.Span, ys.Span);
    }
}
