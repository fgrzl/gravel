using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Storage.InMemory.Sst;

/// <summary>
///     In-memory SST writer implementation for writing entries and sealing into an <see cref="InMemorySst"/> instance.
/// </summary>
/// <param name="onCompleted">Callback invoked when the writer is disposed and the SST is sealed.</param>
public sealed class InMemorySstWriter(Action<InMemorySst> onCompleted) : ISstWriter
{
    readonly List<DbEntry> _entries = [];

    /// <summary>
    ///     Asynchronously writes a sequence of database entries to the in-memory SST.
    /// </summary>
    /// <param name="entries">The async sequence of database entries to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public async ValueTask WriteAsync(IAsyncEnumerable<DbEntry> entries, CancellationToken ct = default)
    {
        await foreach (var e in entries.WithCancellation(ct)) _entries.Add(e);
    }

    /// <summary>
    ///     Flushes any buffered data to the SST. No-op for in-memory implementation.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Disposes the writer, seals the SST, and invokes the completion callback.
    /// </summary>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask DisposeAsync()
    {
        // Convert to (Key,Value) list keeping only puts for legacy in-memory SST representation
        var list = _entries.Where(e => e.Kind == DbEntryKind.Put)
            .Select(e => (e.Key, e.Value)).ToList();
        onCompleted(new InMemorySst(list));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Asynchronously initializes the writer. No-op for in-memory implementation.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }
}
