using Gravel.Internals;

namespace Gravel.Storage.Shared;

/// <summary>
///     Builds a simple block for SST storage, encoding key and handle pairs into a buffer.
/// </summary>
public sealed class SimpleBlockBuilder
{
    readonly MemoryStream _buf = new();

    /// <summary>
    ///     Adds a key and its associated <see cref="BlockHandle" /> to the block.
    /// </summary>
    /// <param name="key">The key to add.</param>
    /// <param name="handle">The block handle associated with the key.</param>
    public void Add(ReadOnlySpan<byte> key, in BlockHandle handle)
    {
        Span<byte> tmp = stackalloc byte[20];
        var n = VarInt.Write32(tmp, (uint)key.Length);
        _buf.Write(tmp[..n]);
        _buf.Write(key);

        Span<byte> hv = stackalloc byte[40];
        var hn = handle.Encode(hv);
        var vn = VarInt.Write32(tmp, (uint)hn);
        _buf.Write(tmp[..vn]);
        _buf.Write(hv[..hn]);
    }

    /// <summary>
    ///     Finalizes the block and returns the encoded buffer as a byte array.
    /// </summary>
    /// <returns>The finished block as a byte array.</returns>
    public byte[] Finish()
    {
        return _buf.ToArray();
    }
}
