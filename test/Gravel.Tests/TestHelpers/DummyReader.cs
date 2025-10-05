using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;

namespace Gravel.TestHelpers;

public sealed class DummyReader : ISstReader
{
    public ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        return new ValueTask<DbEntry?>((DbEntry?)null);
    }

    public IAsyncEnumerable<DbEntry> ReadAllAsync(CancellationToken ct = default)
    {
        return Empty(ct);
    }

    public ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        return new ValueTask<bool>(false);
    }

    public IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes()
    {
        return [];
    }

    public void Dispose()
    {
    }

    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }

    static async IAsyncEnumerable<DbEntry> Empty([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
