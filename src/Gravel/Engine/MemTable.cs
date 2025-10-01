using System.Threading;
using Gravel.Abstractions;
using Gravel.Internals;

namespace Gravel.Engine;

/// <summary>
///     In-memory MemTable backed by a skip list.
///     Stores key/value pairs with sequence numbers for versioning.
///     Uses an EntryPool and ArrayPool
///     <byte>
///         to minimize allocations.
///         Thread-safety: all public methods are synchronized with a lock.
/// </summary>
public sealed class MemTable
{
    const int MaxLevel = 16;
    readonly Node _head = new(null!, MaxLevel); // dummy head
    readonly EntryPool _pool = new();

    readonly object _sync = new();

    // reuse a single update array (protected by same lock) to avoid per-op allocations
    readonly Node?[] _update = new Node?[MaxLevel];
    int _count;
    int _level = 1;

    // Targeted range index for fast coverage checks
    readonly RangeIndex _rangeIndex = new();

    // Diagnostics: track how often Scan() is enumerated
    long _scanEnumerations;
    internal long ScanEnumerations => Interlocked.Read(ref _scanEnumerations);

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ulong seq)
    {
        Upsert(key, value, seq, DbEntryKind.Put);
    }

    public void PutRangeTombstone(ReadOnlySpan<byte> start, ReadOnlySpan<byte> end, ulong seq)
    {
        Upsert(start, end, seq, DbEntryKind.DeleteRange);
    }

    public void PutDeleteTombstone(ReadOnlySpan<byte> key, ulong seq)
    {
        Upsert(key, ReadOnlySpan<byte>.Empty, seq, DbEntryKind.DeleteKey);
    }

    void Upsert(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ulong seq, DbEntryKind kind)
    {
        var k = key.ToArray();
        var v = value.ToArray();

        lock (_sync)
        {
            var update = _update;
            var x = _head;
            var level = _level;

            // search path (cache Forward array locally)
            for (var i = level - 1; i >= 0; i--)
            {
                var forward = x.Forward;
                Node? f;
                while ((f = forward[i]) != null && ByteComparer.Compare(f.Entry.KeySpan, key) < 0)
                {
                    x = f;
                    forward = x.Forward;
                }

                update[i] = x;
            }

            x = x.Forward[0];
            if (x != null && ByteComparer.Compare(x.Entry.KeySpan, key) == 0)
            {
                if (seq >= x.Entry.Seq)
                {
                    x.Entry.Key = k;
                    x.Entry.KeyLen = key.Length;
                    x.Entry.Value = v;
                    x.Entry.ValueLen = value.Length;
                    x.Entry.Seq = seq;
                    x.Entry.Kind = kind;

                    if (kind == DbEntryKind.DeleteRange)
                        _rangeIndex.Add(k, v, seq);
                }

                return;
            }

            var lvl = RandomLevel();
            if (lvl > level)
            {
                for (var i = level; i < lvl; i++) update[i] = _head;
                _level = lvl;
            }

            var entry = _pool.Rent(k, key.Length, v, value.Length, seq, kind);
            var newNode = new Node(entry, lvl);
            for (var i = 0; i < lvl; i++)
            {
                newNode.Forward[i] = update[i]!.Forward[i];
                update[i]!.Forward[i] = newNode;
            }

            if (kind == DbEntryKind.DeleteRange)
                _rangeIndex.Add(k, v, seq);

            _count++;
        }
    }

    public bool TryGet(ReadOnlySpan<byte> key, out ReadOnlyMemory<byte>? value, out ulong seq, out DbEntryKind kind)
    {
        lock (_sync)
        {
            var x = _head;
            var level = _level;
            for (var i = level - 1; i >= 0; i--)
            {
                var forward = x.Forward;
                Node? f;
                while ((f = forward[i]) != null && ByteComparer.Compare(f.Entry.KeySpan, key) < 0)
                {
                    x = f;
                    forward = x.Forward;
                }
            }

            x = x.Forward[0];
            if (x != null && ByteComparer.Compare(x.Entry.KeySpan, key) == 0)
            {
                seq = x.Entry.Seq;
                kind = x.Entry.Kind;
                if (kind == DbEntryKind.Put)
                    value = x.Entry.ValueMemory;
                else if (kind == DbEntryKind.DeleteRange)
                    value = x.Entry.ValueMemory;
                else
                    value = null;
                return true;
            }

            value = null;
            seq = 0;
            kind = 0;
            return false;
        }
    }

    public bool TryGetCoveringRange(ReadOnlySpan<byte> key, out ulong coveringSeq)
    {
        lock (_sync)
        {
            return _rangeIndex.TryGetCoveringSequence(key, out coveringSeq);
        }
    }

    // Internal maintenance hooks for future TTL/epoch-based pruning
    internal int CompactRangesBySequence(ulong minLiveSeq)
    {
        lock (_sync)
        {
            return _rangeIndex.CompactBySequence(minLiveSeq);
        }
    }

    internal int RemoveRangesWhere(Func<byte[], byte[], ulong, bool> predicate)
    {
        lock (_sync)
        {
            return _rangeIndex.RemoveWhere(predicate);
        }
    }

    public bool Delete(ReadOnlySpan<byte> key)
    {
        lock (_sync)
        {
            var update = _update;
            var x = _head;
            var level = _level;
            for (var i = level - 1; i >= 0; i--)
            {
                var forward = x.Forward;
                Node? f;
                while ((f = forward[i]) != null && ByteComparer.Compare(f.Entry.KeySpan, key) < 0)
                {
                    x = f;
                    forward = x.Forward;
                }

                update[i] = x;
            }

            x = x.Forward[0];
            if (x == null || ByteComparer.Compare(x.Entry.KeySpan, key) != 0) return false;

            for (var i = 0; i < _level; i++)
                if (update[i]!.Forward[i] == x)
                    update[i]!.Forward[i] = x.Forward[i];

            while (_level > 1 && _head.Forward[_level - 1] == null) _level--;

            _pool.Return(x.Entry);
            _count--;
            return true;
        }
    }

    public IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value, ulong Seq, DbEntryKind Kind)> Scan(
        ReadOnlyMemory<byte>? start = null, ReadOnlyMemory<byte>? end = null)
    {
        Interlocked.Increment(ref _scanEnumerations);
        // Snapshot entries under lock into arrays so enumeration is safe from concurrent mutation.
        List<(byte[] Key, byte[] Value, ulong Seq, DbEntryKind Kind)> snapshot;
        lock (_sync)
        {
            var startKey = start?.ToArray();
            var endKey = end?.ToArray();
            snapshot = new List<(byte[] Key, byte[] Value, ulong Seq, DbEntryKind Kind)>(_count);

            Node? curr;
            // seek to start
            var x = _head;
            if (startKey != null)
            {
                for (var i = _level - 1; i >= 0; i--)
                    while (x.Forward[i] != null && ByteComparer.Compare(x.Forward[i]!.Entry.KeySpan, startKey) < 0)
                        x = x.Forward[i]!;
                curr = x.Forward[0];
            }
            else
            {
                curr = _head.Forward[0];
            }

            while (curr != null)
            {
                if (endKey != null && ByteComparer.Compare(curr.Entry.KeySpan, endKey) >= 0) break;
                // copy into snapshot arrays to decouple from future mutations
                var keyCopy = curr.Entry.KeySpan.ToArray();
                var valCopy = curr.Entry.ValueSpan.ToArray();
                snapshot.Add((keyCopy, valCopy, curr.Entry.Seq, curr.Entry.Kind));
                curr = curr.Forward[0];
            }
        }

        foreach (var (k, v, s, kind) in snapshot)
            yield return (k, v, s, kind);
    }

    int RandomLevel()
    {
        var lvl = 1;
        // use shared Random to avoid per-instance cost
        while (lvl < MaxLevel && (Random.Shared.Next() & 1) == 1) lvl++;
        return lvl;
    }

    sealed class Node(EntryPool.Entry entry, int level)
    {
        public readonly EntryPool.Entry Entry = entry;
        public readonly Node?[] Forward = new Node?[level];
        public readonly int Level = level;
    }

    sealed class RangeIndex
    {
        readonly List<(byte[] Start, byte[] End, ulong Seq)> _ranges = new();

        public void Add(byte[] start, byte[] end, ulong seq)
        {
            // insert by Start asc then Seq desc to keep local clusters
            int lo = 0, hi = _ranges.Count - 1, pos = _ranges.Count;
            while (lo <= hi)
            {
                var mid = (lo + hi) >>> 1;
                var cmp = ByteComparer.Compare(_ranges[mid].Start, start);
                if (cmp < 0)
                {
                    lo = mid + 1;
                    pos = lo;
                }
                else if (cmp > 0)
                {
                    hi = mid - 1;
                    pos = mid;
                }
                else
                {
                    // equal start: insert before lower seq to keep seq desc grouping
                    // find first position with seq <= incoming seq
                    var p = mid;
                    while (p > 0 && ByteComparer.Compare(_ranges[p - 1].Start, start) == 0 && _ranges[p - 1].Seq < seq)
                        p--;
                    pos = p;
                    break;
                }
            }

            _ranges.Insert(Math.Min(pos, _ranges.Count), (start, end, seq));
        }

        public bool TryGetCoveringSequence(ReadOnlySpan<byte> key, out ulong coveringSeq)
        {
            coveringSeq = 0;
            if (_ranges.Count == 0) return false;

            // find first with Start > key (upper bound) then scan backwards while Start <= key
            int lo = 0, hi = _ranges.Count - 1, ub = _ranges.Count;
            while (lo <= hi)
            {
                var mid = (lo + hi) >>> 1;
                var cmp = ByteComparer.Compare(_ranges[mid].Start, key);
                if (cmp <= 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    ub = mid;
                    hi = mid - 1;
                }
            }

            for (var i = ub - 1; i >= 0; i--)
            {
                var r = _ranges[i];
                if (ByteComparer.Compare(r.Start, key) > 0) break;
                if (ByteComparer.Compare(key, r.End) < 0)
                {
                    if (r.Seq > coveringSeq) coveringSeq = r.Seq;
                }
            }

            return coveringSeq > 0;
        }

        public int CompactBySequence(ulong minLiveSeq)
        {
            return RemoveWhere((s, e, seq) => seq < minLiveSeq);
        }

        public int RemoveWhere(Func<byte[], byte[], ulong, bool> predicate)
        {
            var removed = 0;
            for (var i = _ranges.Count - 1; i >= 0; i--)
            {
                var r = _ranges[i];
                if (predicate(r.Start, r.End, r.Seq))
                {
                    _ranges.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }
    }
}