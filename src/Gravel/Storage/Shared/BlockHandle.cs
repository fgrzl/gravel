using Gravel.Internals;

namespace Gravel.Storage.Shared;

/// <summary>
///     Represents a handle to a block in an SST file, including its offset and size.
///     Provides encoding helpers for serialization.
/// </summary>
public readonly struct BlockHandle(ulong offset, ulong size)
{
    /// <summary>
    ///     The offset of the block within the SST file.
    /// </summary>
    public readonly ulong Offset = offset;

    /// <summary>
    ///     The size of the block in bytes.
    /// </summary>
    public readonly ulong Size = size;

    /// <summary>
    ///     Encodes the block handle into the provided span using varint encoding.
    /// </summary>
    /// <param name="dst">The destination span to write to.</param>
    /// <returns>The number of bytes written.</returns>
    public int Encode(Span<byte> dst)
    {
        var n = 0;
        n += VarInt.Write64(dst[n..], Offset);
        n += VarInt.Write64(dst[n..], Size);
        return n;
    }
}
