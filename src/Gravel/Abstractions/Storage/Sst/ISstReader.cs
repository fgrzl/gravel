namespace Gravel.Abstractions.Storage.Sst;

public interface ISstReader : IAsyncInitializable, IDisposable
{
    ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);
    IAsyncEnumerable<DbEntry> ReadAllAsync(CancellationToken ct = default);
    ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default);

    // New: expose range tombstones contained in this SST
    IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes();
}
