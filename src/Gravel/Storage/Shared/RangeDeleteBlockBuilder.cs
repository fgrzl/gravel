using System.Buffers.Binary;
using Gravel.Internals;

namespace Gravel.Storage.Shared;

/// <summary>
///     Builds a simple range-deletion block: sequence of entries
///     [startLen varint][start bytes][endLen varint][end bytes][seq ulong LE]
///     Entries are sorted by start key to enable binary search in reader.
/// </summary>
public sealed class RangeDeleteBlockBuilder
{
    readonly List<(byte[] Start, byte[] End, ulong Seq)> _entries = [];

    /// <summary>
    ///     Gets the number of recorded range tombstones.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    ///     Adds a new range deletion tombstone.
    /// </summary>
    /// <param name="start">The inclusive start key.</param>
    /// <param name="end">The exclusive end key.</param>
    /// <param name="seq">The sequence number of the tombstone.</param>
    public void Add(ReadOnlySpan<byte> start, ReadOnlySpan<byte> end, ulong seq)
    {
        _entries.Add((start.ToArray(), end.ToArray(), seq));
    }

    /// <summary>
    ///     Finalizes the block and returns its serialized bytes.
    /// </summary>
    /// <returns>Serialized range-delete block.</returns>
    public byte[] Finish()
    {
        if (_entries.Count == 0)
            return [];

        _entries.Sort((a, b) => ByteComparer.Compare(a.Start, b.Start));
        using var ms = new MemoryStream();
        Span<byte> tmp = stackalloc byte[12];
        Span<byte> seqBuf = stackalloc byte[8];
        foreach (var (s, e, seq) in _entries)
        {
            var n = VarInt.Write32(tmp, (uint)s.Length);
            ms.Write(tmp[..n]);
            ms.Write(s);

            n = VarInt.Write32(tmp, (uint)e.Length);
            ms.Write(tmp[..n]);
            ms.Write(e);

            BinaryPrimitives.WriteUInt64LittleEndian(seqBuf, seq);
            ms.Write(seqBuf);
        }

        return ms.ToArray();
    }
}
