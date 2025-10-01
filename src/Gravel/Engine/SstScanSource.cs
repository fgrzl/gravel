using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Internals;

namespace Gravel.Engine;

sealed class SstScanSource : IScanSource
{
    readonly CancellationToken _ct;
    readonly ReadOnlyMemory<byte>? _end;
    readonly ConcurrentQueue<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value, ulong Seq, DbEntryKind Kind)> _buffer = new();
    readonly ManualResetEventSlim _dataReady = new(false);
    volatile bool _completed;

    public int Precedence { get; }
    public bool HasItem { get; private set; }
    public ReadOnlyMemory<byte> Key { get; private set; }
    public ReadOnlyMemory<byte> Value { get; private set; }
    public DbEntryKind Kind { get; private set; }
    public ulong Sequence { get; private set; }

    SstScanSource(int precedence, ReadOnlyMemory<byte>? end, CancellationToken ct)
    {
        Precedence = precedence;
        _end = end;
        _ct = ct;
    }

    public bool MoveNext()
    {
        // Non-blocking fast path
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

        // If producer not done, wait briefly for data
        if (!_completed)
        {
            _dataReady.Wait(5);
            _dataReady.Reset();
            if (_buffer.TryDequeue(out e))
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
        }

        // No more data and producer completed
        HasItem = false;
        return false;
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
                .OrderBy(x => x.Start, Comparer<ReadOnlyMemory<byte>>.Create((a,b) => ByteComparer.Compare(a.Span,b.Span)))
                .ThenByDescending(x => x.Seq)
                .ToList();

            var rangeIdx = 0;
            var points = reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

            // Advance to first point at/after start and skip range entries in point stream
            async Task<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value, ulong Seq, DbEntryKind Kind)?> NextPointAsync()
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
                else emitPoint = nextPoint != null;

                if (emitPoint)
                {
                    sink._buffer.Enqueue(nextPoint!.Value);
                    sink._dataReady.Set();
                    nextPoint = await NextPointAsync().ConfigureAwait(false);
                }
                else
                {
                    sink._buffer.Enqueue((nextRange!.Value.Key, nextRange.Value.End, nextRange.Value.Seq, DbEntryKind.DeleteRange));
                    sink._dataReady.Set();
                    PrimeRange();
                }
            }
        }
        finally
        {
            sink._completed = true;
            sink._dataReady.Set();
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

        // Start producer and also prefill a small batch so MoveNext sees data immediately
        _ = ProduceAsync(src, r, start, end, ct);
        // Warm-up: wait briefly for first batch
        await Task.Delay(1, ct);

        // Prime first item into public state
        src.MoveNext();
        return src;
    }
}