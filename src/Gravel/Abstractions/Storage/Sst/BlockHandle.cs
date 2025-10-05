using Gravel.Internals;

namespace Gravel.Abstractions.Storage.Sst;

public readonly struct BlockHandle(ulong offset, ulong size)
{
    public readonly ulong Offset = offset;
    public readonly ulong Size = size;

    public int Encode(Span<byte> dst)
    {
        var n = 0;
        n += VarInt.Write64(dst[n..], Offset);
        n += VarInt.Write64(dst[n..], Size);
        return n;
    }
}
