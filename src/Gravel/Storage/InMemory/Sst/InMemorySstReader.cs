using System.Runtime.CompilerServices;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Storage.InMemory.Sst;

public sealed class InMemorySstReader(InMemorySst sst) : ISstReader
{
    public ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        if (sst.TryGet(key.Span, out var val))
            return ValueTask.FromResult<DbEntry?>(DbEntry.Put(key, val!, 0));
        return ValueTask.FromResult<DbEntry?>(null);
    }

    public async IAsyncEnumerable<DbEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var (k, v) in sst.Entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return DbEntry.Put(k, v, 0); // in-memory SST still only stores puts
            await Task.Yield();
        }
    }

    public ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        return ValueTask.FromResult(true); // no bloom filtering
    }

    public IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes()
    {
        return [];
    }

    public void Dispose()
    {
    }
}