using System.Buffers;
using System.Runtime.CompilerServices;

namespace Gravel.Internals;

/// <summary>
///     Helper for efficient temporary buffers.
///     Provides sync-friendly <see cref="BufferScope" /> (stackalloc or pooled)
///     and async-friendly <see cref="ArrayPoolScope" /> (pooled only).
/// </summary>
public static class Buf
{
    /// <summary>
    ///     Rent a buffer for synchronous code.
    ///     Uses stackalloc if below threshold, otherwise ArrayPool.
    /// </summary>
    public static BufferScope Rent(int length)
    {
        return new BufferScope(length);
    }

    public static BufferScope Rent(int length, bool clearOnDispose)
    {
        return new BufferScope(length, clearOnDispose);
    }

    /// <summary>
    ///     Rent a buffer for async code.
    ///     Always uses ArrayPool since stackalloc can’t cross awaits.
    /// </summary>
    public static ArrayPoolScope AsyncRent(int length)
    {
        return new ArrayPoolScope(length);
    }

    public static ArrayPoolScope AsyncRent(int length, bool clearOnDispose)
    {
        return new ArrayPoolScope(length, clearOnDispose);
    }


    public sealed class ArrayPoolScope : IDisposable
    {
        readonly bool _clearOnDispose;
        readonly int _length;
        byte[]? _buffer;

        internal ArrayPoolScope(int length, bool clearOnDispose = false)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            _length = length;
            _clearOnDispose = clearOnDispose;
            if (length == 0)
            {
                // represent "no buffer rented" with null to avoid returning Array.Empty to the pool
                _buffer = null;
                return;
            }

            _buffer = ArrayPool<byte>.Shared.Rent(length);
        }

        public Memory<byte> Memory => _buffer == null ? Memory<byte>.Empty : _buffer.AsMemory(0, _length);
        public Span<byte> Span => _buffer == null ? Span<byte>.Empty : _buffer.AsSpan(0, _length);
        public byte[] Buffer => _buffer ?? [];

        public void Dispose()
        {
            if (_buffer == null) return;

            if (_clearOnDispose)
                Array.Clear(_buffer, 0, _length);

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }


    public ref struct BufferScope
    {
        /// <summary>The span representing the rented buffer.</summary>
        public Span<byte> Span { get; }

        readonly bool _clearOnDispose;

        /// <summary>
        ///     Rents a buffer of at least <paramref name="length" /> bytes from the shared pool.
        /// </summary>
        /// <param name="length">The minimum required buffer length.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferScope(int length, bool clearOnDispose = false)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            _clearOnDispose = clearOnDispose;

            if (length == 0)
            {
                // represent "no buffer rented" with null and an empty span
                Buffer = null;
                Span = Span<byte>.Empty;
                return;
            }

            Buffer = ArrayPool<byte>.Shared.Rent(length);
            Span = Buffer.AsSpan(0, length);
        }

        /// <summary>
        ///     Returns the rented buffer (if any) back to the pool.
        /// </summary>
        /// <remarks>
        ///     This method is picked up by the compiler's "using declaration" pattern
        ///     even though the type does not implement <see cref="IDisposable" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (Buffer == null)
                return;

            if (_clearOnDispose)
                Array.Clear(Buffer, 0, Span.Length);

            ArrayPool<byte>.Shared.Return(Buffer);
            Buffer = null;
        }

        /// <summary>
        ///     Implicitly converts the scope to its <see cref="Span{T}" />.
        /// </summary>
        public static implicit operator Span<byte>(BufferScope scope)
        {
            return scope.Span;
        }

        public byte[]? Buffer { get; private set; }
    }
}
