using Gravel.Internals;

namespace Gravel.Abstractions.Storage.Sst;

sealed class SimpleBlockBuilder
{
    readonly MemoryStream _buf = new();

    public void Add(ReadOnlySpan<byte> key, in BlockHandle handle)
    {
        Span<byte> tmp = stackalloc byte[20];
        var n = Varint.Write32(tmp, (uint)key.Length);
        _buf.Write(tmp[..n]);
        _buf.Write(key);

        Span<byte> hv = stackalloc byte[40];
        var hn = handle.Encode(hv);
        var vn = Varint.Write32(tmp, (uint)hn);
        _buf.Write(tmp[..vn]);
        _buf.Write(hv[..hn]);
    }

    public byte[] Finish()
    {
        return _buf.ToArray();
    }

    public void Reset()
    {
        _buf.SetLength(0);
    }
}
