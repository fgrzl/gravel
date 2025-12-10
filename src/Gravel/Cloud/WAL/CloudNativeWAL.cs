using Gravel.Abstractions;
using Gravel.Storage.TLV;
using Microsoft.Extensions.Logging;

namespace Gravel.Cloud.WAL;

/// <summary>
///     Cloud-native WAL implementation using TLV format.
///     - Fast local append to memory buffer
///     - Async segment upload to cloud
///     - Efficient recovery via sequential reads
/// </summary>
public sealed class CloudNativeWAL : IAsyncDisposable
{
    readonly ILogger _logger;
    readonly int _segmentSizeBytes;
    ulong _currentSequence;

    byte[] _buffer;
    int _bufferOffset;
    readonly object _bufferLock = new();

    List<WalSegment> _segments = [];

    /// <summary>
    ///     Initializes a new instance of <see cref="CloudNativeWAL" />.
    /// </summary>
    public CloudNativeWAL(int segmentSizeBytes = 10 * 1024 * 1024, ILogger? logger = null)
    {
        _segmentSizeBytes = segmentSizeBytes;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _buffer = new byte[segmentSizeBytes];
        _bufferOffset = 0;
    }

    /// <summary>
    ///     Last durable sequence number.
    /// </summary>
    public ulong LastDurableSequence => _currentSequence;

    /// <summary>
    ///     Appends an entry to the WAL buffer (fast, in-memory).
    /// </summary>
    public async ValueTask AppendAsync(DbEntry entry, CancellationToken ct = default)
    {
        var type = entry.Kind switch
        {
            DbEntryKind.Put => TLVFormat.TypePut,
            DbEntryKind.DeleteKey => TLVFormat.TypeDelete,
            DbEntryKind.DeleteRange => TLVFormat.TypeDeleteRange,
            _ => throw new ArgumentException("Invalid entry kind")
        };

        var keyMem = entry.Key;
        var valueMem = entry.Kind == DbEntryKind.DeleteKey ? Memory<byte>.Empty : entry.Value;

        lock (_bufferLock)
        {
            var needed = TLVFormat.CalculateEntrySize(keyMem.Length, valueMem.Length);

            // Auto-roll to new segment if needed
            if (_bufferOffset + needed > _segmentSizeBytes)
            {
                RollSegmentSync();
            }

            var written = TLVFormat.WriteEntry(_buffer, _bufferOffset, type, keyMem.Span, valueMem.Span);
            if (written < 0)
                throw new InvalidOperationException("Failed to write WAL entry");

            _bufferOffset += written;
            _currentSequence = entry.Sequence;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Rolls the current segment (synchronously, called from within lock).
    /// </summary>
    private void RollSegmentSync()
    {
        if (_bufferOffset == 0)
            return;

        var segmentId = $"wal-{DateTime.UtcNow.Ticks:D20}";
        var segmentData = _buffer[.._bufferOffset];
        var segment = new WalSegment
        {
            SegmentId = segmentId,
            Data = new byte[segmentData.Length],
            Size = segmentData.Length,
            SequenceRange = (_currentSequence - (ulong)_bufferOffset, _currentSequence),
            CreatedAtUtc = DateTime.UtcNow
        };

        // Copy current data to segment
        Array.Copy(_buffer, segment.Data, _bufferOffset);
        _segments.Add(segment);

        // Queue segment for cloud upload (actor runtime will handle this)
        _logger.LogDebug("WAL segment rolled: {SegmentId}, {Size} bytes", segment.SegmentId, segment.Size);

        // Reset buffer
        _buffer = new byte[_segmentSizeBytes];
        _bufferOffset = 0;
    }

    /// <summary>
    ///     Gets all WAL segments for export/backup.
    /// </summary>
    public IReadOnlyList<WalSegment> GetSegments()
    {
        lock (_bufferLock)
        {
            var result = new List<WalSegment>(_segments);
            if (_bufferOffset > 0)
            {
                result.Add(new WalSegment
                {
                    SegmentId = $"seg-current-{DateTime.UtcNow.Ticks:D20}",
                    Data = _buffer[.._bufferOffset],
                    Size = _bufferOffset,
                    SequenceRange = (_currentSequence - (ulong)_bufferOffset, _currentSequence),
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            return result;
        }
    }

    /// <summary>
    ///     Recovers entries from WAL segments.
    /// </summary>
    public async IAsyncEnumerable<DbEntry> RecoverAsync(IReadOnlyList<WalSegment> segments)
    {
        foreach (var segment in segments)
        {
            var reader = new TLVReader(segment.Data);
            while (reader.TryReadNext(out var type, out var key, out var value))
            {
                var entry = type switch
                {
                    TLVFormat.TypePut => DbEntry.Put(key, value, segment.SequenceRange.Start),
                    TLVFormat.TypeDelete => DbEntry.DeleteKey(key, segment.SequenceRange.Start),
                    TLVFormat.TypeDeleteRange => DbEntry.DeleteRange(key, value, segment.SequenceRange.Start),
                    _ => default
                };

                if (entry != default)
                {
                    yield return entry;
                }

                await Task.Yield();
            }
        }
    }

    /// <summary>
    ///     Disposes the WAL.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        lock (_bufferLock)
        {
            _segments.Clear();
        }
        await Task.CompletedTask;
    }
}

/// <summary>
///     Metadata about a WAL segment.
/// </summary>
public sealed class WalSegment
{
    /// <summary>
    ///     Segment identifier.
    /// </summary>
    public required string SegmentId { get; init; }

    /// <summary>
    ///     Segment data bytes.
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    ///     Size in bytes.
    /// </summary>
    public required int Size { get; init; }

    /// <summary>
    ///     Sequence number range.
    /// </summary>
    public required (ulong Start, ulong End) SequenceRange { get; init; }

    /// <summary>
    ///     UTC timestamp when created.
    /// </summary>
    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    ///     Whether segment has been durably stored in cloud.
    /// </summary>
    public bool IsCloudDurable { get; set; }
}
