using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Storage.InMemory.Sst;

public sealed class InMemorySstWriter(Action<InMemorySst> onCompleted) : ISstWriter
{
    readonly List<DbEntry> _entries = [];

    public async ValueTask WriteAsync(IAsyncEnumerable<DbEntry> entries, CancellationToken ct = default)
    {
        await foreach (var e in entries.WithCancellation(ct)) _entries.Add(e);
    }

    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        // Convert to (Key,Value) list keeping only puts for legacy in-memory SST representation
        var list = _entries.Where(e => e.Kind == DbEntryKind.Put)
            .Select(e => (e.Key, e.Value)).ToList();
        onCompleted(new InMemorySst(list));
        return ValueTask.CompletedTask;
    }
}
