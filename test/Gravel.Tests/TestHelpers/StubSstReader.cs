using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Internals;

namespace Gravel.TestHelpers;

sealed class StubSstReader(IEnumerable<DbEntry> entries) : ISstReader
{
    readonly List<DbEntry> _entries = [.. entries];

    public ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        var found = _entries.FirstOrDefault(e => ByteComparer.Compare(e.Key.Span, key.Span) == 0);
        if (found.Key.IsEmpty && found.Value.IsEmpty && found is { Sequence: 0, Kind: 0 })
            return ValueTask.FromResult<DbEntry?>(null);
        return ValueTask.FromResult<DbEntry?>(found);
    }

    public async IAsyncEnumerable<DbEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in _entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
            await Task.Yield();
        }
    }

    public ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        return ValueTask.FromResult(true);
    }

    public IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes()
    {
        return [];
    }

    public void Dispose()
    {
    }
}
