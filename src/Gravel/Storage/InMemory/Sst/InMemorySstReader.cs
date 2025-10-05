using System.Runtime.CompilerServices;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Storage.InMemory.Sst;

/// <summary>
///     In-memory SST reader implementation for reading entries from an <see cref="InMemorySst" /> instance.
/// </summary>
/// <param name="sst">The in-memory SST to read from.</param>
public sealed class InMemorySstReader(InMemorySst sst) : ISstReader
{
    /// <summary>
    ///     Asynchronously gets the entry for the specified key, or null if not found.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The entry if found, otherwise null.</returns>
    public ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        if (sst.TryGet(key.Span, out var val))
            return ValueTask.FromResult<DbEntry?>(DbEntry.Put(key, val!, 0));
        return ValueTask.FromResult<DbEntry?>(null);
    }

    /// <summary>
    ///     Asynchronously reads all entries in the in-memory SST.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of all entries.</returns>
    public async IAsyncEnumerable<DbEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var (k, v) in sst.Entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return DbEntry.Put(k, v, 0);
            await Task.Yield();
        }
    }

    /// <summary>
    ///     Asynchronously checks if the SST might contain the specified key. Always returns true (no bloom filtering).
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the key might be present, otherwise false.</returns>
    public ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        return ValueTask.FromResult(true);
    }

    /// <summary>
    ///     Gets the list of range tombstones (range deletes) contained in this SST file. Always empty for in-memory SST.
    /// </summary>
    /// <returns>A read-only list of tuples containing start key, end key, and sequence number.</returns>
    public IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes()
    {
        return [];
    }

    /// <summary>
    ///     Disposes the reader. No-op for in-memory implementation.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    ///     Asynchronously initializes the reader. No-op for in-memory implementation.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A completed <see cref="ValueTask" />.</returns>
    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }
}
