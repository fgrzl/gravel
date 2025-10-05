using Gravel.Abstractions;
using Gravel.Exceptions;

namespace Gravel.Engine.Managers;

class MemTableManager
{
    public MemTable MemTable { get; private set; } = new();

    public int Count => MemTable.Count;

    public void ApplyStagedEntries(List<DbEntry> staging)
    {
        foreach (var e in staging)
        {
            switch (e.Kind)
            {
                case DbEntryKind.Put:
                    MemTable.Put(e.Key.Span, e.Value.Span, e.Sequence);
                    break;
                case DbEntryKind.DeleteKey:
                    MemTable.PutDeleteTombstone(e.Key.Span, e.Sequence);
                    break;
                case DbEntryKind.DeleteRange:
                    MemTable.PutRangeTombstone(e.Key.Span, e.Value.Span, e.Sequence);
                    break;
                default:
                    throw new GravelArgumentOutOfRangeException();
            }
        }
    }

    public ReadOnlyMemory<byte>? TryGetFromMemTable(ReadOnlyMemory<byte> key, ulong coveringRangeSeq)
    {
        if (MemTable.TryGet(key.Span, out var mtValue, out var mtSeq, out var mtKind))
        {
            if (mtKind != DbEntryKind.Put)
                return null;
            return mtSeq >= coveringRangeSeq ? mtValue : null;
        }

        return null;
    }

    public MemTable GetMemTable()
    {
        return MemTable;
    }

    public void ResetMemTable()
    {
        MemTable = new MemTable();
    }

    // New helpers to avoid external code directly touching MemTable
    public bool ContainsKey(ReadOnlyMemory<byte> key)
    {
        return MemTable.TryGet(key.Span, out _, out _, out _);
    }

    public bool IsPut(ReadOnlyMemory<byte> key)
    {
        return MemTable.TryGet(key.Span, out _, out _, out var kind) && kind == DbEntryKind.Put;
    }

    public void ApplyEntry(DbEntry e)
    {
        ApplyStagedEntries(new List<DbEntry> { e });
    }

    public bool TryGetCoveringRange(ReadOnlySpan<byte> key, out ulong coveringSeq)
    {
        return MemTable.TryGetCoveringRange(key, out coveringSeq);
    }

    public bool TryGet(ReadOnlySpan<byte> key, out ReadOnlyMemory<byte>? value, out ulong seq, out DbEntryKind kind)
    {
        var found = MemTable.TryGet(key, out var v, out var s, out var k);
        value = v;
        seq = s;
        kind = k;
        return found;
    }
}
