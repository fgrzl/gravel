using Gravel.Abstractions;
using Gravel.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Engine;

/// <summary>
///     In-memory sorted data structure (skip-list based) for buffering writes before SST flush.
///     Maintains entries sorted by key for efficient range operations.
/// </summary>
public sealed class MemTable
{
    readonly ILogger _logger;
    readonly SortedDictionary<ByteKey, (ReadOnlyMemory<byte> Value, ulong Sequence)> _entries;
    int _approximateSize;
    const int TargetFlushSize = 64 * 1024 * 1024; // 64MB default

    /// <summary>
    ///     Initializes a new <see cref="MemTable" />.
    /// </summary>
    public MemTable(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _entries = new SortedDictionary<ByteKey, (ReadOnlyMemory<byte>, ulong)>(new ByteKeyComparer());
        _approximateSize = 0;
    }

    /// <summary>
    ///     Number of entries in the memtable.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    ///     Whether memtable is empty.
    /// </summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>
    ///     Approximate size in bytes.
    /// </summary>
    public int ApproximateSize => _approximateSize;

    /// <summary>
    ///     Whether a flush should be triggered (size threshold exceeded).
    /// </summary>
    public bool ShouldFlush() => _approximateSize >= TargetFlushSize;

    /// <summary>
    ///     Puts a key-value pair (insert or update).
    /// </summary>
    public void Put(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value, ulong sequence)
    {
        var byteKey = new ByteKey(key);

        if (_entries.TryGetValue(byteKey, out var existing))
        {
            // Update: account for size difference
            _approximateSize -= existing.Value.Length + 8;
        }

        _entries[byteKey] = (value, sequence);
        _approximateSize += key.Length + value.Length + 8;

        _logger.LogDebug("MemTable.Put: key={KeyLen} value={ValueLen} size={Size}",
            key.Length, value.Length, _approximateSize);
    }

    /// <summary>
    ///     Gets a value by key, returns null if not found.
    /// </summary>
    public (ReadOnlyMemory<byte> Value, ulong Sequence)? Get(ReadOnlyMemory<byte> key)
    {
        var byteKey = new ByteKey(key);
        return _entries.TryGetValue(byteKey, out var entry) ? entry : null;
    }

    /// <summary>
    ///     Deletes a key (stores tombstone internally).
    /// </summary>
    public void Delete(ReadOnlyMemory<byte> key, ulong sequence)
    {
        var byteKey = new ByteKey(key);

        if (_entries.TryGetValue(byteKey, out var existing))
        {
            _approximateSize -= existing.Value.Length;
        }

        // Store empty value as tombstone
        _entries[byteKey] = (Memory<byte>.Empty, sequence);
        _approximateSize += key.Length + 8;

        _logger.LogDebug("MemTable.Delete: key={KeyLen}", key.Length);
    }

    /// <summary>
    ///     Marks a range as deleted (simplified: just marks boundaries).
    /// </summary>
    public void DeleteRange(ReadOnlyMemory<byte> start, ReadOnlyMemory<byte> end, ulong sequence)
    {
        // Store range delete markers at boundaries
        var startKey = new ByteKey(start);
        var endKey = new ByteKey(end);

        _entries[startKey] = (Memory<byte>.Empty, sequence);
        _entries[endKey] = (Memory<byte>.Empty, sequence);

        _approximateSize += start.Length + end.Length + 16;

        _logger.LogDebug("MemTable.DeleteRange: start={StartLen} end={EndLen}",
            start.Length, end.Length);
    }

    /// <summary>
    ///     Gets all entries for flushing to SST.
    /// </summary>
    public IEnumerable<DbEntry> GetEntries()
    {
        foreach (var kvp in _entries)
        {
            var key = kvp.Key.Data;
            var (value, sequence) = kvp.Value;

            if (value.Length == 0 && key.Length > 0)
            {
                // Tombstone
                yield return DbEntry.DeleteKey(key, sequence);
            }
            else if (value.Length > 0)
            {
                yield return DbEntry.Put(key, value, sequence);
            }
        }
    }

    /// <summary>
    ///     Clears all entries (for resetting after flush).
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        _approximateSize = 0;
    }

    /// <summary>
    ///     Wrapper for byte-based key comparison and storage.
    /// </summary>
    private class ByteKey
    {
        public byte[] Data { get; }

        public ByteKey(ReadOnlyMemory<byte> data)
        {
            Data = data.ToArray();
        }

        public override bool Equals(object? obj)
        {
            return obj is ByteKey other && Data.SequenceEqual(other.Data);
        }

        public override int GetHashCode()
        {
            return Crc32C.Compute(Data).GetHashCode();
        }
    }

    /// <summary>
    ///     Comparer for ByteKey that uses lexicographic byte ordering.
    /// </summary>
    private class ByteKeyComparer : IComparer<ByteKey>
    {
        public int Compare(ByteKey? x, ByteKey? y)
        {
            if (x == null || y == null)
                return (x == null ? 1 : 0) - (y == null ? 1 : 0);

            // Use lexicographic comparison
            var xData = x.Data;
            var yData = y.Data;

            int minLen = Math.Min(xData.Length, yData.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (xData[i] != yData[i])
                    return xData[i].CompareTo(yData[i]);
            }

            return xData.Length.CompareTo(yData.Length);
        }
    }
}
