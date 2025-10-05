using Gravel.Abstractions;
using Gravel.Exceptions;

namespace Gravel.Engine.Managers;

class MemTableManager
{
    public MemTable MemTable { get; private set; } = new();

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
}
