using Gravel.Internals;

namespace Gravel.Abstractions.Storage.Sst;

public readonly struct BlockHandle
{
    public readonly ulong Offset;
    public readonly ulong Size;

    public BlockHandle(ulong offset, ulong size)
    {
        Offset = offset;
        Size = size;
    }

    public int Encode(Span<byte> dst)
    {
        var n = 0;
        n += Varint.Write64(dst[n..], Offset);
        n += Varint.Write64(dst[n..], Size);
        return n;
    }
}