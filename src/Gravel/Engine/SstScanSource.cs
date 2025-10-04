using System.Collections.Concurrent;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Internals;

namespace Gravel.Engine;

sealed class SstScanSource : IScanSource
{
    readonly ConcurrentQueue<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value, ulong Seq, DbEntryKind Kind)>
        _buffer = new();

    readonly CancellationToken _ct;
    readonly ReadOnlyMemory<byte>? _end;
    readonly SemaphoreSlim _itemAvailable = new(0);
    volatile bool _completed;

    SstScanSource(int precedence, ReadOnlyMemory<byte>? end, CancellationToken ct)
    {
        Precedence = precedence;
        _end = end;
        _ct = ct;
    }

    public int Precedence { get; }
    public bool HasItem { get; private set; }
    public ReadOnlyMemory<byte> Key { get; private set; }
    public ReadOnlyMemory<byte> Value { get; private set; }
    public DbEntryKind Kind { get; private set; }
    public ulong Sequence { get; private set; }

    public bool MoveNext()
    {
        // Loop until we either dequeue an item, or the producer has completed
        while (true)
        {
            if (_buffer.TryDequeue(out var e))
            {
                if (_end.HasValue && ByteComparer.Compare(e.Key.Span, _end.Value.Span) >= 0)
                {
                    HasItem = false;
                    return false;
                }

                Key = e.Key;
                Value = e.Value;
                Kind = e.Kind;
                Sequence = e.Seq;
                HasItem = true;
                return true;
            }

            if (_completed)
            {
                HasItem = false;
                return false;
            }

            // Wait for producer to signal an available item or completion. Short timeout to stay responsive.
            _itemAvailable.Wait(50);
        }
    }

    static async Task ProduceAsync(
        SstScanSource sink,
        ISstReader reader,
        ReadOnlyMemory<byte>? start,
        ReadOnlyMemory<byte>? end,
        CancellationToken ct)
    {
        try
        {
            // Prepare ranges
            var ranges = reader.GetRangeDeletes()
                .Select(x => (x.Start, x.End, x.Seq))
                .Where(x => !(end.HasValue && ByteComparer.Compare(x.Start.Span, end.Value.Span) >= 0))
                .Where(x => !(start.HasValue && ByteComparer.Compare(x.End.Span, start.Value.Span) <= 0))
                .OrderBy(x => x.Start,
                    Comparer<ReadOnlyMemory<byte>>.Create((a, b) => ByteComparer.Compare(a.Span, b.Span)))
                .ThenByDescending(x => x.Seq)
                .ToList();

            var rangeIdx = 0;
            var points = reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

            // Advance to first point at/after start and skip range entries in point stream
            async Task<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value, ulong Seq, DbEntryKind Kind)?>
                NextPointAsync()
            {
                while (await points.MoveNextAsync().ConfigureAwait(false))
                {
                    var e = points.Current;
                    if (e.Kind == DbEntryKind.DeleteRange) continue;
                    if (start.HasValue && ByteComparer.Compare(e.Key.Span, start.Value.Span) < 0) continue;
                    if (end.HasValue && ByteComparer.Compare(e.Key.Span, end.Value.Span) >= 0) return null;
                    return (e.Key, e.Value, e.Sequence, e.Kind);
                }

                return null;
            }

            var nextPoint = await NextPointAsync().ConfigureAwait(false);

            // Prepare first range marker within bounds
            (ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> End, ulong Seq)? nextRange = null;

            void PrimeRange()
            {
                while (rangeIdx < ranges.Count)
                {
                    var r = ranges[rangeIdx++];
                    var emitKey = start.HasValue && ByteComparer.Compare(r.Start.Span, start.Value.Span) < 0
                        ? start.Value
                        : r.Start;
                    if (end.HasValue && ByteComparer.Compare(emitKey.Span, end.Value.Span) >= 0) continue;
                    nextRange = (emitKey, r.End, r.Seq);
                    return;
                }

                nextRange = null;
            }

            PrimeRange();

            // Monotonic merge: always emit the smaller key; for ties choose higher seq first
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (nextPoint == null && nextRange == null) break;

                bool emitPoint;
                if (nextPoint != null && nextRange != null)
                {
                    var cmp = ByteComparer.Compare(nextPoint.Value.Key.Span, nextRange.Value.Key.Span);
                    if (cmp < 0) emitPoint = true;
                    else if (cmp > 0) emitPoint = false;
                    else emitPoint = nextPoint.Value.Seq >= nextRange.Value.Seq;
                }
                else
                {
                    emitPoint = nextPoint != null;
                }

                if (emitPoint)
                {
                    sink._buffer.Enqueue(nextPoint!.Value);
                    // signal one available item
                    try
                    {
                        sink._itemAvailable.Release();
                    }
                    catch
                    {
                    }

                    nextPoint = await NextPointAsync().ConfigureAwait(false);
                }
                else
                {
                    sink._buffer.Enqueue((nextRange!.Value.Key, nextRange.Value.End, nextRange.Value.Seq,
                        DbEntryKind.DeleteRange));
                    try
                    {
                        sink._itemAvailable.Release();
                    }
                    catch
                    {
                    }

                    PrimeRange();
                }
            }
        }
        finally
        {
            sink._completed = true;
            // release waiting MoveNext() calls so they can observe completion
            try
            {
                sink._itemAvailable.Release();
            }
            catch
            {
            }
        }
    }

    public static async Task<SstScanSource> CreateAsync(
        ISstReader r,
        int prec,
        ReadOnlyMemory<byte>? start,
        ReadOnlyMemory<byte>? end,
        CancellationToken ct = default)
    {
        var src = new SstScanSource(prec, end, ct);

        // Produce all items into the buffer before returning to avoid races in tests
        await ProduceAsync(src, r, start, end, ct).ConfigureAwait(false);

        // Prime first item into public state
        src.MoveNext();
        return src;
    }
}
