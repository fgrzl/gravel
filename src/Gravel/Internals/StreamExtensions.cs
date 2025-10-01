using System.Buffers.Binary;

namespace Gravel.Internals;

static class StreamExtensions
{
    public static async ValueTask<int> ReadInt32Async(this Stream s, CancellationToken ct = default)
    {
        using var bufScope = Buf.AsyncRent(4);
        await ReadFullAsync(s, bufScope.Memory, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt32LittleEndian(bufScope.Span);
    }

    public static async ValueTask<byte[]> ReadBytesAsync(this Stream s, int len, CancellationToken ct = default)
    {
        var buf = new byte[len];
        await ReadFullAsync(s, buf, ct).ConfigureAwait(false);
        return buf;
    }

    static async ValueTask ReadFullAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        await ReadFullAsync(s, buf.AsMemory(), ct).ConfigureAwait(false);
    }

    static async ValueTask ReadFullAsync(Stream s, Memory<byte> buf, CancellationToken ct)
    {
        var read = 0;
        while (read < buf.Length)
        {
            var r = await s.ReadAsync(buf[read..], ct).ConfigureAwait(false);
            if (r == 0) throw new EndOfStreamException();
            read += r;
        }
    }

    // convenience method to read into a Span-backed buffer and return bytes read
    public static int Read(this Stream s, Span<byte> buffer)
    {
        using var tmp = Buf.Rent(buffer.Length);
        var read = s.Read(tmp.Span);
        if (read > 0)
            tmp.Span[..read].CopyTo(buffer);
        return read;
    }
}