using System.Runtime.CompilerServices;

namespace Gravel.Abstractions;

/// <summary>
///     Lightweight stack-only view of an SST entry used during parsing and fast iteration.
///     This type is a ref struct to avoid heap allocations; it must not be stored on the heap.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly ref struct DbEntryLight(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ulong seq, DbEntryKind kind)
{
    /// <summary>
    ///     The entry key. Backed by a <see cref="ReadOnlySpan{Byte}" /> that is valid for the
    ///     lifetime of the current parse/iteration scope.
    /// </summary>
    public readonly ReadOnlySpan<byte> Key = key;

    /// <summary>
    ///     The entry value. For <see cref="DbEntryKind.DeleteKey" /> this is empty. For
    ///     <see cref="DbEntryKind.DeleteRange" /> this contains the range end (exclusive).
    /// </summary>
    public readonly ReadOnlySpan<byte> Value = value;

    /// <summary>
    ///     Sequence number associated with this entry used for versioning.
    /// </summary>
    public readonly ulong Sequence = seq;

    /// <summary>
    ///     The kind of database entry (Put, DeleteKey, DeleteRange).
    /// </summary>
    public readonly DbEntryKind Kind = kind;

    /// <summary>
    ///     Materializes this lightweight entry into a heap-allocated <see cref="DbEntry" />.
    ///     Use this when the entry must be returned from the current parsing scope or stored
    ///     beyond the lifetime of the source buffer.
    /// </summary>
    /// <returns>A new <see cref="DbEntry" /> with owned memory for key and value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DbEntry ToOwned()
    {
        return Kind switch
        {
            DbEntryKind.Put => DbEntry.Put(Key.ToArray(), Value.ToArray(), Sequence),
            DbEntryKind.DeleteKey => DbEntry.DeleteKey(Key.ToArray(), Sequence),
            DbEntryKind.DeleteRange => DbEntry.DeleteRange(Key.ToArray(), Value.ToArray(), Sequence),
            _ => throw new InvalidDataException($"Unknown DbEntryKind {Kind}")
        };
    }

    /// <summary>
    ///     Returns a concise diagnostic representation of the entry including kind,
    ///     sequence number and sizes of the key and value.
    /// </summary>
    /// <returns>A short human-readable string for debugging.</returns>
    public override string ToString()
    {
        return $"{Kind} seq={Sequence} key={Key.Length}B val={Value.Length}B";
    }
}
