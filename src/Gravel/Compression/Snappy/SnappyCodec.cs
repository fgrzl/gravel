using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Gravel.Internals;

namespace Gravel.Compression.Snappy;

/// <summary>
///     Snappy (unframed) helper with full back-reference compression.
///     Optimized for fewer allocations and faster copy/scan loops.
///     Provides static methods for compressing and decompressing data, and a stream wrapper.
/// </summary>
public static class SnappyCodec
{
    const int HashBits = 16; // 64k table

    // Public API: compress/decompress
    /// <summary>
    ///     Compresses the input data using the Snappy algorithm (unframed).
    /// </summary>
    /// <param name="input">The input data to compress.</param>
    /// <returns>A compressed byte array containing the Snappy-encoded data.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
            return [0];

        using var buffer = Buf.Rent(input.Length + 10);
        var outBuf = buffer.Span;
        var offset = WriteVarInt(outBuf, (uint)input.Length);
        offset += WriteLiteral(outBuf[offset..], input);
        return outBuf[..offset].ToArray();

    }

    /// <summary>
    ///     Compresses the input data into a caller-provided buffer using the Snappy algorithm (unframed).
    /// </summary>
    /// <param name="input">The input data to compress.</param>
    /// <param name="destination">The buffer to receive the compressed data.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
    /// <returns>True if compression succeeded and the buffer was large enough; otherwise, false.</returns>
    public static bool TryCompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        if (input.IsEmpty)
        {
            if (destination.Length < 1)
            {
                bytesWritten = 0;
                return false;
            }

            destination[0] = 0;
            bytesWritten = 1;
            return true;
        }

        if (destination.Length < input.Length + 10)
        {
            bytesWritten = 0;
            return false;
        }

        var offset = WriteVarInt(destination, (uint)input.Length);
        offset += WriteLiteral(destination[offset..], input);
        bytesWritten = offset;
        return true;
    }

    /// <summary>
    ///     Decompresses Snappy-compressed data (unframed) into a new byte array.
    /// </summary>
    /// <param name="input">The compressed input data.</param>
    /// <returns>The decompressed byte array.</returns>
    /// <exception cref="InvalidDataException">Thrown if the input is not valid Snappy data.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        var pos = 0;
        if (!TryReadVarInt(input, ref pos, out var expected))
            throw new InvalidDataException("Invalid Snappy stream: cannot read uncompressed length.");
        var output = new byte[expected];
        if (!TryDecompress(input, output, out var written) || written != expected)
            throw new InvalidDataException("Decompressed length mismatch.");
        return output;
    }

    /// <summary>
    ///     Decompresses Snappy-compressed data (unframed) into a caller-provided buffer.
    /// </summary>
    /// <param name="input">The compressed input data.</param>
    /// <param name="destination">The buffer to receive the decompressed data.</param>
    /// <param name="bytesWritten">The number of bytes written to <paramref name="destination" />.</param>
    /// <returns>True if decompression succeeded and the buffer was large enough; otherwise, false.</returns>
    public static bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> destination, out int bytesWritten)
    {
        var pos = 0;
        if (!TryReadVarInt(input, ref pos, out var expected) || destination.Length < expected)
        {
            bytesWritten = 0;
            return false;
        }

        var written = 0;
        while (pos < input.Length)
        {
            var tag = input[pos++];
            var kind = tag & 0x03;
            if (kind == 0)
            {
                if (TryDecodeLiteral(input, ref pos, destination, ref written))
                    continue;

                bytesWritten = 0;
                return false;
            }

            if (TryDecodeCopy(input, ref pos, kind, tag, destination, ref written))
                continue;
            bytesWritten = 0;
            return false;
        }

        bytesWritten = written;
        return written == expected;
    }

    /// <summary>
    ///     Decodes a literal block from the Snappy stream.
    /// </summary>
    /// <param name="input">The compressed input data.</param>
    /// <param name="pos">The current position in the input span (updated).</param>
    /// <param name="destination">The buffer to receive the decompressed data.</param>
    /// <param name="written">The current write position in the destination buffer (updated).</param>
    /// <returns>True if decoding succeeded; otherwise, false.</returns>
    static bool TryDecodeLiteral(ReadOnlySpan<byte> input, ref int pos, Span<byte> destination, ref int written)
    {
        var n = input.Length;
        int tag = input[pos - 1];
        var len = tag >> 2 & 0x3F;
        if (len < 60) len++;
        else
        {
            var extra = len - 59;
            if (extra < 1 || extra > 4 || pos + extra > n)
                return false;

            uint lenm1 = 0;
            for (var i = 0; i < extra; i++)
                lenm1 |= (uint)input[pos++] << 8 * i;
            len = (int)(lenm1 + 1);
        }

        if (pos + len > n || written + len > destination.Length) return false;
        input.Slice(pos, len).CopyTo(destination.Slice(written, len));
        pos += len;
        written += len;
        return true;
    }

    /// <summary>
    ///     Decodes a copy block from the Snappy stream.
    /// </summary>
    /// <param name="input">The compressed input data.</param>
    /// <param name="pos">The current position in the input span (updated).</param>
    /// <param name="kind">The copy block kind (1, 2, or 3).</param>
    /// <param name="tag">The block tag byte.</param>
    /// <param name="destination">The buffer to receive the decompressed data.</param>
    /// <param name="written">The current write position in the destination buffer (updated).</param>
    /// <returns>True if decoding succeeded; otherwise, false.</returns>
    static bool TryDecodeCopy(
        ReadOnlySpan<byte> input, ref int pos, int kind, int tag, Span<byte> destination, ref int written)
    {
        var n = input.Length;
        int offset, copyLen;
        switch (kind)
        {
            case 1:
                {
                    copyLen = (tag >> 2 & 0x07) + 4;
                    if (pos >= n)
                        return false;
                    offset = input[pos++];
                    break;
                }
            case 2:
                {
                    copyLen = (tag >> 2 & 0x3F) + 1;
                    if (pos + 2 > n) return false;
                    offset = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(pos, 2));
                    pos += 2;
                    break;
                }
            default:
                {
                    copyLen = (tag >> 2 & 0x3F) + 1;
                    if (pos + 4 > n) return false;
                    offset = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(pos, 4));
                    pos += 4;
                    break;
                }
        }

        if (offset == 0 || written - offset < 0 || written + copyLen > destination.Length) return false;
        for (var i = 0; i < copyLen; i++)
            destination[written + i] = destination[written - offset + i];
        written += copyLen;
        return true;
    }

    /// <summary>
    ///     Writes a variable-length integer to the destination buffer.
    /// </summary>
    /// <param name="dst">The buffer to write to.</param>
    /// <param name="value">The value to encode.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int WriteVarInt(Span<byte> dst, uint value)
    {
        var i = 0;
        while (value >= 0x80)
        {
            dst[i++] = (byte)(value | 0x80u);
            value >>= 7;
        }

        dst[i++] = (byte)value;
        return i;
    }

    /// <summary>
    ///     Reads a variable-length integer from the source buffer.
    /// </summary>
    /// <param name="src">The buffer to read from.</param>
    /// <param name="pos">The current position in the buffer (updated).</param>
    /// <param name="value">The decoded value.</param>
    /// <returns>True if decoding succeeded; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryReadVarInt(ReadOnlySpan<byte> src, ref int pos, out int value)
    {
        uint result = 0;
        int shift = 0, start = pos;
        while (pos < src.Length)
        {
            var b = src[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                value = (int)result;
                return true;
            }

            shift += 7;
            if (shift > 35) break;
        }

        pos = start;
        value = 0;
        return false;
    }

    /// <summary>
    ///     Writes a literal block to the destination buffer.
    /// </summary>
    /// <param name="dst">The buffer to write to.</param>
    /// <param name="literal">The literal data to encode.</param>
    /// <returns>The number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int WriteLiteral(Span<byte> dst, ReadOnlySpan<byte> literal)
    {
        int len = literal.Length, pos = 0;
        if (len <= 60)
        {
            dst[pos++] = (byte)(((len - 1) << 2));
        }
        else
        {
            var lenm1 = (uint)(len - 1);
            var extra = lenm1 <= 0xFF ? 1 : lenm1 <= 0xFFFF ? 2 : lenm1 <= 0xFFFFFF ? 3 : 4;
            dst[pos++] = (byte)(((60 + (extra - 1)) << 2));
            for (var i = 0; i < extra; i++)
                dst[pos++] = (byte)(lenm1 >> 8 * i);
        }

        literal.CopyTo(dst[pos..]);
        return pos + len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int WriteCopy1(Span<byte> dst, int len, int offset)
    {
        if (len < 4) len = 4;
        if (len > 11) len = 11;
        dst[0] = (byte)(len - 4 << 2 | 1);
        dst[1] = (byte)(offset & 0xFF);
        return 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int WriteCopy2(Span<byte> dst, int len, int offset)
    {
        if (len < 1) len = 1;
        if (len > 64) len = 64;
        dst[0] = (byte)(len - 1 << 2 | 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(1, 2), (ushort)offset);
        return 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int WriteCopy4(Span<byte> dst, int len, int offset)
    {
        if (len < 1) len = 1;
        if (len > 64) len = 64;
        dst[0] = (byte)(len - 1 << 2 | 3);
        BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(1, 4), offset);
        return 5;
    }

    /// <summary>
    ///     Provides a Stream wrapper similar to existing .NET compression streams for Snappy compression and decompression.
    /// </summary>
    public sealed class SnappyStream : Stream
    {
        readonly Stream _baseStream;
        readonly bool _leaveOpen;
        readonly CompressionMode _mode;
        readonly MemoryStream? _writeBuffer; // used for Compress mode
        int _decompPos;
        byte[]? _decompressed; // used for Decompress mode
        bool _disposed;

        /// <summary>
        ///     Initializes a new instance of <see cref="SnappyStream" /> for compression or decompression.
        /// </summary>
        /// <param name="stream">The underlying stream.</param>
        /// <param name="mode">Compression or decompression mode.</param>
        /// <param name="leaveOpen">Whether to leave the underlying stream open on dispose.</param>
        public SnappyStream(Stream stream, CompressionMode mode, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream, nameof(stream));
            _baseStream = stream;
            _mode = mode;
            _leaveOpen = leaveOpen;

            if (mode == CompressionMode.Compress)
                _writeBuffer = new MemoryStream();
        }

        /// <summary>
        ///     Gets a value indicating whether the stream supports reading.
        /// </summary>
        public override bool CanRead => !_disposed && _mode == CompressionMode.Decompress && _baseStream.CanRead;

        /// <summary>
        ///     Gets a value indicating whether the stream supports seeking (always false).
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        ///     Gets a value indicating whether the stream supports writing.
        /// </summary>
        public override bool CanWrite => !_disposed && _mode == CompressionMode.Compress && _baseStream.CanWrite;

        /// <summary>
        ///     Gets the length of the stream (not supported).
        /// </summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        ///     Gets or sets the position within the stream (not supported).
        /// </summary>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        ///     Flushes the stream and writes any buffered data.
        /// </summary>
        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SnappyStream));
            if (_mode == CompressionMode.Compress)
            {
                if (_writeBuffer == null) return;
                if (_writeBuffer.Length == 0) return;
                var data = _writeBuffer.ToArray();
                var comp = Compress(data);
                _baseStream.Write(comp, 0, comp.Length);
                _baseStream.Flush();
                _writeBuffer.SetLength(0);
            }
            else
            {
                _baseStream.Flush();
            }
        }

        /// <summary>
        ///     Reads decompressed data from the stream into the buffer.
        /// </summary>
        /// <param name="buffer">The buffer to read into.</param>
        /// <param name="offset">The offset in the buffer.</param>
        /// <param name="count">The maximum number of bytes to read.</param>
        /// <returns>The number of bytes read.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SnappyStream));
            if (_mode != CompressionMode.Decompress)
                throw new NotSupportedException("Stream not opened for decompression.");
            ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();

            EnsureDecompressed();
            if (_decompressed == null || _decompPos >= _decompressed.Length) return 0;
            var toCopy = Math.Min(count, _decompressed.Length - _decompPos);
            Buffer.BlockCopy(_decompressed, _decompPos, buffer, offset, toCopy);
            _decompPos += toCopy;
            return toCopy;
        }

        /// <summary>
        ///     Writes data to the stream for compression.
        /// </summary>
        /// <param name="buffer">The buffer containing data to write.</param>
        /// <param name="offset">The offset in the buffer.</param>
        /// <param name="count">The number of bytes to write.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SnappyStream));
            if (_mode != CompressionMode.Compress)
                throw new NotSupportedException("Stream not opened for compression.");
            ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();

            _writeBuffer!.Write(buffer, offset, count);
        }

        /// <summary>
        ///     Ensures the stream is decompressed and ready for reading.
        /// </summary>
        void EnsureDecompressed()
        {
            if (_decompressed != null) return;
            using var ms = new MemoryStream();
            _baseStream.CopyTo(ms);
            var comp = ms.ToArray();
            _decompressed = Decompress(comp);
            _decompPos = 0;
        }

        /// <summary>
        ///     Releases resources used by the stream.
        /// </summary>
        /// <param name="disposing">True to release managed resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
                try
                {
                    if (_mode == CompressionMode.Compress && _writeBuffer is { Length: > 0 })
                    {
                        var data = _writeBuffer.ToArray();
                        var comp = Compress(data);
                        _baseStream.Write(comp, 0, comp.Length);
                        _baseStream.Flush();
                    }
                }
                finally
                {
                    if (!_leaveOpen) _baseStream.Dispose();
                    _writeBuffer?.Dispose();
                }

            _disposed = true;
            base.Dispose(disposing);
        }

        /// <summary>
        ///     Seeks to a position in the stream (not supported).
        /// </summary>
        /// <param name="offset">The offset to seek to.</param>
        /// <param name="origin">The seek origin.</param>
        /// <returns>Not supported.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Sets the length of the stream (not supported).
        /// </summary>
        /// <param name="value">The length to set.</param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
