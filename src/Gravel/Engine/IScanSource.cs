using Gravel.Abstractions;

namespace Gravel.Engine;

interface IScanSource
{
    bool HasItem { get; }
    ReadOnlyMemory<byte> Key { get; }
    ReadOnlyMemory<byte> Value { get; }
    DbEntryKind Kind { get; }
    ulong Sequence { get; }
    int Precedence { get; }
    bool MoveNext();
}
