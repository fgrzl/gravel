using Gravel.Abstractions;

namespace Gravel.Engine;

readonly struct Applied(
    MutationOp op,
    ReadOnlyMemory<byte> key,
    ReadOnlyMemory<byte> value,
    ReadOnlyMemory<byte> rangeEnd,
    ulong seq)
{
    public readonly MutationOp Op = op;
    public readonly ReadOnlyMemory<byte> Key = key;
    public readonly ReadOnlyMemory<byte> Value = value;
    public readonly ReadOnlyMemory<byte> RangeEnd = rangeEnd;
    public readonly ulong Sequence = seq;
}
