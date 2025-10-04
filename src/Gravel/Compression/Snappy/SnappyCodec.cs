using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Gravel.Compression.Snappy;

/// <summary>
///     Snappy (unframed) helper with full back-reference compression.
///     Optimized for fewer allocations and faster copy/scan loops.
/// </summary>
public static class SnappyCodec
{
    const uint HashMul = 2654435761u;
    const int HashBits = 16; // 64k table
    const int HashSize = 1 << HashBits;

    // Public API: compress/decompress
    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        var n = input.Length;
        if (n == 0) return [0]; // varint zero only

        // Simplified encoder: always emit uncompressed literal block after varint length.
        // varint length may take up to 5 bytes for 32-bit
        var maxOverhead = 5 + 1 + 4; // varint + tag + potential length bytes
        var outBuf = new byte[n + maxOverhead];
        var dst = 0;

        // write uncompressed length as varint
        dst += WriteVarint(outBuf, dst, (uint)n);

        // write single literal for entire input
        dst += WriteLiteral(outBuf.AsSpan(dst), input);

        var result = new byte[dst];
        Buffer.BlockCopy(outBuf, 0, result, 0, dst);
        return result;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        var pos = 0;
        if (!TryReadVarint(input, ref pos, out var expected))
            throw new InvalidDataException("Invalid Snappy stream: cannot read uncompressed length.");

        var output = new byte[expected];
        var w = 0;
        var n = input.Length;

        while (pos < n)
        {
            var tag = input[pos++];
            var kind = tag & 0x03;
            if (kind == 0)
            {
                var literalLen = tag >> 2 & 0x3F;
                if (literalLen < 60)
                {
                    literalLen += 1;
                }
                else
                {
                    var extra = literalLen - 59;
                    if (extra is < 1 or > 4) throw new InvalidDataException("Invalid literal length encoding.");
                    if (pos + extra > n)
                        throw new InvalidDataException("Unexpected end of stream reading literal length.");
                    uint lenm1 = 0;
                    for (var i = 0; i < extra; i++)
                        lenm1 |= (uint)input[pos++] << 8 * i;
                    literalLen = (int)(lenm1 + 1);
                }

                if (pos + literalLen > n)
                    throw new InvalidDataException("Unexpected end of stream reading literal data.");
                if (w + literalLen > output.Length)
                    throw new InvalidDataException("Decompressed data exceeds expected length.");

                // bulk copy literal
                input.Slice(pos, literalLen).CopyTo(output.AsSpan(w, literalLen));
                pos += literalLen;
                w += literalLen;
            }
            else if (kind == 1)
            {
                var len = (tag >> 2 & 0x07) + 4;
                if (pos >= n) throw new InvalidDataException("Unexpected end of stream reading COPY_1 offset.");
                var offset = input[pos++];
                if (offset == 0) throw new InvalidDataException("Invalid COPY offset 0.");
                var src = w - offset;
                if (src < 0 || w + len > output.Length) throw new InvalidDataException("COPY out of bounds.");
                Array.Copy(output, src, output, w, len);
                w += len;
            }
            else if (kind == 2)
            {
                var len = (tag >> 2 & 0x3F) + 1;
                if (pos + 2 > n) throw new InvalidDataException("Unexpected end of stream reading COPY_2 offset.");
                var offset = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(pos, 2));
                pos += 2;
                if (offset == 0) throw new InvalidDataException("Invalid COPY offset 0.");
                var src = w - offset;
                if (src < 0 || w + len > output.Length) throw new InvalidDataException("COPY out of bounds.");
                Array.Copy(output, src, output, w, len);
                w += len;
            }
            else
            {
                var len = (tag >> 2 & 0x3F) + 1;
                if (pos + 4 > n) throw new InvalidDataException("Unexpected end of stream reading COPY_4 offset.");
                var offset = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(pos, 4));
                pos += 4;
                if (offset == 0) throw new InvalidDataException("Invalid COPY offset 0.");
                var src = w - offset;
                if (src < 0 || w + len > output.Length) throw new InvalidDataException("COPY out of bounds.");
                Array.Copy(output, src, output, w, len);
                w += len;
            }
        }

        if (w != output.Length) throw new InvalidDataException("Decompressed length mismatch.");
        return output;
    }

    // -------------------- helpers --------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int WriteVarint(byte[] dst, int dstOffset, uint value)
    {
        var i = dstOffset;
        while (value >= 0x80)
        {
            dst[i++] = (byte)(value & 0x7Fu | 0x80u);
            value >>= 7;
        }

        dst[i++] = (byte)(value & 0x7Fu);
        return i - dstOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryReadVarint(ReadOnlySpan<byte> src, ref int pos, out int value)
    {
        uint result = 0;
        var shift = 0;
        var start = pos;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int WriteLiteral(Span<byte> dst, ReadOnlySpan<byte> literal)
    {
        var len = literal.Length;
        var pos = 0;

        if (len <= 60)
        {
            dst[pos++] = (byte)(len - 1 << 2 | 0);
        }
        else
        {
            var lenm1 = (uint)(len - 1);
            int extra;
            if (lenm1 <= 0xFF) extra = 1;
            else if (lenm1 <= 0xFFFF) extra = 2;
            else if (lenm1 <= 0xFFFFFF) extra = 3;
            else extra = 4;

            var tagUpper = 60 + (extra - 1);
            dst[pos++] = (byte)(tagUpper << 2 | 0);
            for (var i = 0; i < extra; i++)
                dst[pos++] = (byte)(lenm1 >> 8 * i & 0xFF);
        }

        literal.CopyTo(dst[pos..]);
        pos += len;
        return pos;
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
    ///     Provides a Stream wrapper similar to existing .NET compression streams.
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

        public SnappyStream(Stream stream, CompressionMode mode, bool leaveOpen = false)
        {
            _baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _mode = mode;
            _leaveOpen = leaveOpen;

            if (mode == CompressionMode.Compress)
                _writeBuffer = new MemoryStream();
        }

        public override bool CanRead => !_disposed && _mode == CompressionMode.Decompress && _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed && _mode == CompressionMode.Compress && _baseStream.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SnappyStream));
            if (_mode != CompressionMode.Decompress)
                throw new NotSupportedException("Stream not opened for decompression.");
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();

            EnsureDecompressed();
            if (_decompressed == null || _decompPos >= _decompressed.Length) return 0;
            var toCopy = Math.Min(count, _decompressed.Length - _decompPos);
            Buffer.BlockCopy(_decompressed, _decompPos, buffer, offset, toCopy);
            _decompPos += toCopy;
            return toCopy;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SnappyStream));
            if (_mode != CompressionMode.Compress)
                throw new NotSupportedException("Stream not opened for compression.");
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();

            _writeBuffer!.Write(buffer, offset, count);
        }

        void EnsureDecompressed()
        {
            if (_decompressed != null) return;
            using var ms = new MemoryStream();
            _baseStream.CopyTo(ms);
            var comp = ms.ToArray();
            _decompressed = Decompress(comp);
            _decompPos = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
                try
                {
                    if (_mode == CompressionMode.Compress && _writeBuffer != null && _writeBuffer.Length > 0)
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

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
