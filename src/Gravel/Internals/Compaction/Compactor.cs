using System.Runtime.CompilerServices;
using Gravel.Abstractions;
using Gravel.Engine;

namespace Gravel.Internals.Compaction;

public static class Compactor
{
    public static int EstimateMergedCount(List<SstFile> files)
    {
        return files.Count * 1024;
    }

    public static async IAsyncEnumerable<DbEntry> MergeLevelFilesAsync(List<SstFile> files,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Build enumerators for point entries and synthetic range entries from each file
        var enumerators = new List<IAsyncEnumerator<DbEntry>>();
        foreach (var f in files)
        {
            // Points stream
            var points = f.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);
            enumerators.Add(points);

            // Ranges stream (reader may or may not provide via ReadAllAsync; ensure inclusion)
            var ranges = f.Reader.GetRangeDeletes();
            if (ranges.Count > 0)
            {
                async IAsyncEnumerable<DbEntry> RangeStream()
                {
                    foreach (var (s, e, seq) in ranges)
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return DbEntry.DeleteRange(s, e, seq);
                        await Task.Yield();
                    }
                }

                enumerators.Add(RangeStream().GetAsyncEnumerator(ct));
            }
        }

        try
        {
            // Min-heap by user key lexicographic order
            var heap = new List<(DbEntry Entry, int SrcIdx)>();
            for (var i = 0; i < enumerators.Count; i++)
                if (await enumerators[i].MoveNextAsync())
                    heap.Add((enumerators[i].Current, i));

            heap.Sort((a, b) => ByteComparer.Compare(a.Entry.Key.Span, b.Entry.Key.Span));

            // Track active range tombstones (highest seq wins) while merging
            var activeRanges = new List<(byte[] Start, byte[] End, ulong Seq)>();

            while (heap.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                var (firstEntry, firstSrc) = heap[0];
                heap.RemoveAt(0);
                var currentKey = firstEntry.Key;

                // Choose winner among same-key entries: highest seq; delete-key beats put on tie
                var winner = firstEntry;

                // advance first source
                if (await enumerators[firstSrc].MoveNextAsync())
                {
                    var next = enumerators[firstSrc].Current;
                    var idxIns = heap.BinarySearch((next, firstSrc),
                        Comparer<(DbEntry Entry, int SrcIdx)>.Create((x, y) =>
                            ByteComparer.Compare(x.Entry.Key.Span, y.Entry.Key.Span)));
                    if (idxIns < 0) idxIns = ~idxIns;
                    heap.Insert(idxIns, (next, firstSrc));
                }

                // gather duplicates for same key
                while (heap.Count > 0 && ByteComparer.Compare(heap[0].Entry.Key.Span, currentKey.Span) == 0)
                {
                    var (dupEntry, dupSrc) = heap[0];
                    heap.RemoveAt(0);

                    if (dupEntry.Sequence > winner.Sequence ||
                        (dupEntry.Sequence == winner.Sequence && dupEntry.Kind == DbEntryKind.DeleteKey &&
                         winner.Kind == DbEntryKind.Put))
                        winner = dupEntry;

                    if (await enumerators[dupSrc].MoveNextAsync())
                    {
                        var next2 = enumerators[dupSrc].Current;
                        var idxIns2 = heap.BinarySearch((next2, dupSrc),
                            Comparer<(DbEntry Entry, int SrcIdx)>.Create((x, y) =>
                                ByteComparer.Compare(x.Entry.Key.Span, y.Entry.Key.Span)));
                        if (idxIns2 < 0) idxIns2 = ~idxIns2;
                        heap.Insert(idxIns2, (next2, dupSrc));
                    }
                }

                // Remove expired ranges before checking coverage (where end <= current key)
                activeRanges.RemoveAll(r => ByteComparer.Compare(r.End, currentKey.Span) <= 0);

                if (winner.Kind == DbEntryKind.DeleteRange)
                {
                    // Insert into active set and emit so writer can persist to range block
                    InsertOrUpdateRange(activeRanges, winner.Key.ToArray(), winner.Value.ToArray(), winner.Sequence);
                    yield return winner;
                    continue;
                }

                if (winner.Kind == DbEntryKind.DeleteKey)
                {
                    yield return winner; // keep delete-keys
                    continue;
                }

                // Winner is a Put: check if masked by any active higher-seq range
                var covered = activeRanges.Exists(r =>
                    ByteComparer.Compare(r.Start, currentKey.Span) <= 0 &&
                    ByteComparer.Compare(currentKey.Span, r.End) < 0 &&
                    r.Seq > winner.Sequence);
                if (!covered) yield return winner;
            }
        }
        finally
        {
            foreach (var e in enumerators)
                try
                {
                    await e.DisposeAsync();
                }
                catch
                {
                }
        }
    }

    static void InsertOrUpdateRange(List<(byte[] Start, byte[] End, ulong Seq)> ranges, byte[] start, byte[] end,
        ulong seq)
    {
        // keep list sorted by start; insert position via binary search
        int lo = 0, hi = ranges.Count - 1, pos = ranges.Count;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var cmp = ByteComparer.Compare(ranges[mid].Start, start);
            if (cmp <= 0)
            {
                lo = mid + 1;
                pos = lo;
            }
            else
            {
                hi = mid - 1;
                pos = mid;
            }
        }

        ranges.Insert(Math.Min(pos, ranges.Count), (start, end, seq));
    }
}