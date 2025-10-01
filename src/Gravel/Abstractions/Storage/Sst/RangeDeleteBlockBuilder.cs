using System.Buffers.Binary;
using Gravel.Internals;

namespace Gravel.Abstractions.Storage.Sst;

/// <summary>
///     Builds a simple range-deletion block: sequence of entries
///     [startLen varint][start bytes][endLen varint][end bytes][seq ulong LE]
///     Entries are sorted by start key to enable binary search in reader.
/// </summary>
sealed class RangeDeleteBlockBuilder
{
    readonly List<(byte[] Start, byte[] End, ulong Seq)> _entries = new();

    public int Count => _entries.Count;

    public void Add(ReadOnlySpan<byte> start, ReadOnlySpan<byte> end, ulong seq)
    {
        _entries.Add((start.ToArray(), end.ToArray(), seq));
    }

    public byte[] Finish()
    {
        if (_entries.Count == 0) return Array.Empty<byte>();
        _entries.Sort((a, b) => ByteComparer.Compare(a.Start, b.Start));
        using var ms = new MemoryStream();
        Span<byte> tmp = stackalloc byte[12];
        foreach (var (s, e, seq) in _entries)
        {
            var n = Varint.Write32(tmp, (uint)s.Length);
            ms.Write(tmp[..n]);
            ms.Write(s);

            n = Varint.Write32(tmp, (uint)e.Length);
            ms.Write(tmp[..n]);
            ms.Write(e);

            Span<byte> seqBuf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(seqBuf, seq);
            ms.Write(seqBuf);
        }

        return ms.ToArray();
    }
}