using Gravel.Abstractions;

namespace Gravel.Engine;

sealed class MemTableScanSource : IScanSource
{
    readonly IEnumerator<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value, ulong Seq, DbEntryKind Kind)> _it;

    public MemTableScanSource(MemTable mt, int prec, ReadOnlyMemory<byte>? start, ReadOnlyMemory<byte>? end)
    {
        _it = mt.Scan(start, end).GetEnumerator();
        Precedence = prec;
        MoveNext();
    }

    public int Precedence { get; }
    public bool HasItem { get; private set; }
    public ReadOnlyMemory<byte> Key { get; private set; }
    public ReadOnlyMemory<byte> Value { get; private set; }
    public DbEntryKind Kind { get; private set; }
    public ulong Sequence { get; private set; }

    public bool MoveNext()
    {
        if (_it.MoveNext())
        {
            Key = _it.Current.Key;
            Value = _it.Current.Value;
            Kind = _it.Current.Kind;
            Sequence = _it.Current.Seq;
            HasItem = true;
            return true;
        }

        HasItem = false;
        return false;
    }
}