using Gravel.Storage.TLV;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Gravel.Storage.Local;

/// <summary>
///     Local-only WAL implementation for single-machine deployments.
///     Uses TLV format for efficient, zero-copy serialization.
///     All segments stored on local disk.
/// </summary>
public sealed class LocalWAL : IAsyncDisposable
{
    readonly string _walDir;
    readonly int _segmentSizeBytes;
    readonly ILogger _logger;
    ulong _currentSequence;

    byte[] _buffer;
    int _bufferOffset;
    readonly object _bufferLock = new();

    List<LocalWalSegment> _segments = [];

    /// <summary>
    ///     Initializes a new instance of <see cref="LocalWAL" />.
    /// </summary>
    public LocalWAL(string walDir, int segmentSizeBytes = 10 * 1024 * 1024, ILogger? logger = null)
    {
        _walDir = walDir ?? throw new ArgumentNullException(nameof(walDir));
        _segmentSizeBytes = segmentSizeBytes;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        Directory.CreateDirectory(_walDir);
        _buffer = new byte[segmentSizeBytes];
        _bufferOffset = 0;

        LoadExistingSegments();
    }

    /// <summary>
    ///     Gets the last durable sequence number.
    /// </summary>
    public ulong LastDurableSequence => _currentSequence;

    /// <summary>
    ///     Appends an entry to the WAL (fast, in-memory buffer).
    /// </summary>
    public async ValueTask AppendAsync(Abstractions.DbEntry entry, CancellationToken ct = default)
    {
        var type = entry.Kind switch
        {
            Abstractions.DbEntryKind.Put => TLVFormat.TypePut,
            Abstractions.DbEntryKind.DeleteKey => TLVFormat.TypeDelete,
            Abstractions.DbEntryKind.DeleteRange => TLVFormat.TypeDeleteRange,
            _ => throw new ArgumentException("Invalid entry kind")
        };

        var keyMem = entry.Key;
        var valueMem = entry.Kind == Abstractions.DbEntryKind.DeleteKey
            ? System.Memory<byte>.Empty
            : entry.Value;

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
    ///     Flushes the current buffer to disk immediately.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken ct = default)
    {
        lock (_bufferLock)
        {
            if (_bufferOffset > 0)
            {
                var segmentPath = Path.Combine(_walDir, $"wal-{_currentSequence:D20}.seg");
                File.WriteAllBytes(segmentPath, _buffer[.._bufferOffset]);
                _logger.LogDebug("WAL segment flushed: {Path}", segmentPath);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Rolls current buffer to a new segment file (synchronously, called from lock).
    /// </summary>
    private void RollSegmentSync()
    {
        if (_bufferOffset == 0)
            return;

        var segmentId = $"wal-{DateTime.UtcNow.Ticks:D20}";
        var segmentPath = Path.Combine(_walDir, segmentId + ".seg");

        // Write buffer to disk
        File.WriteAllBytes(segmentPath, _buffer[.._bufferOffset]);

        var segment = new LocalWalSegment
        {
            Path = segmentPath,
            SegmentId = segmentId,
            Size = _bufferOffset,
            SequenceRange = (_currentSequence - (ulong)_bufferOffset, _currentSequence),
            CreatedAtUtc = DateTime.UtcNow
        };

        _segments.Add(segment);
        _logger.LogDebug("WAL segment rolled: {SegmentId}, {Size} bytes", segment.SegmentId, segment.Size);

        // Reset buffer
        _buffer = new byte[_segmentSizeBytes];
        _bufferOffset = 0;
    }

    /// <summary>
    ///     Recovers entries from all WAL segments in order.
    /// </summary>
    public async IAsyncEnumerable<Abstractions.DbEntry> RecoverAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var segments = _segments.OrderBy(s => s.SequenceRange.Start).ToList();

        foreach (var segment in segments)
        {
            var data = await File.ReadAllBytesAsync(segment.Path, ct).ConfigureAwait(false);
            var reader = new TLVReader(data);

            while (reader.TryReadNext(out var type, out var key, out var value))
            {
                var entry = type switch
                {
                    TLVFormat.TypePut => Abstractions.DbEntry.Put(key, value, segment.SequenceRange.Start),
                    TLVFormat.TypeDelete => Abstractions.DbEntry.DeleteKey(key, segment.SequenceRange.Start),
                    TLVFormat.TypeDeleteRange => Abstractions.DbEntry.DeleteRange(key, value, segment.SequenceRange.Start),
                    _ => default
                };

                if (entry != default)
                {
                    yield return entry;
                }

                await Task.Yield();
            }
        }

        // Also recover from current buffer if not yet rolled
        if (_bufferOffset > 0)
        {
            var reader = new TLVReader(_buffer.AsMemory(0, _bufferOffset));
            while (reader.TryReadNext(out var type, out var key, out var value))
            {
                var entry = type switch
                {
                    TLVFormat.TypePut => Abstractions.DbEntry.Put(key, value, _currentSequence),
                    TLVFormat.TypeDelete => Abstractions.DbEntry.DeleteKey(key, _currentSequence),
                    TLVFormat.TypeDeleteRange => Abstractions.DbEntry.DeleteRange(key, value, _currentSequence),
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
    ///     Deletes all WAL segments (after successful backup/checkpoint).
    /// </summary>
    public void DeleteAllSegments()
    {
        lock (_bufferLock)
        {
            foreach (var seg in _segments)
            {
                try
                {
                    File.Delete(seg.Path);
                    _logger.LogDebug("Deleted WAL segment: {Path}", seg.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete WAL segment: {Path}", seg.Path);
                }
            }
            _segments.Clear();
        }
    }

    private void LoadExistingSegments()
    {
        try
        {
            var segmentFiles = Directory.GetFiles(_walDir, "wal-*.seg")
                .OrderBy(Path.GetFileName)
                .ToList();

            foreach (var file in segmentFiles)
            {
                var fileInfo = new FileInfo(file);
                var segmentId = Path.GetFileNameWithoutExtension(file);

                _segments.Add(new LocalWalSegment
                {
                    Path = file,
                    SegmentId = segmentId,
                    Size = (int)fileInfo.Length,
                    CreatedAtUtc = fileInfo.CreationTimeUtc
                });

                _logger.LogDebug("Loaded existing WAL segment: {SegmentId}", segmentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading existing WAL segments");
        }
    }

    /// <summary>
    ///     Disposes the WAL, flushing any pending data.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);
    }
}

/// <summary>
///     Local WAL segment metadata.
/// </summary>
public sealed class LocalWalSegment
{
    /// <summary>
    ///     Full file system path to the segment.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    ///     Segment identifier.
    /// </summary>
    public required string SegmentId { get; init; }

    /// <summary>
    ///     Size in bytes.
    /// </summary>
    public required int Size { get; init; }

    /// <summary>
    ///     Sequence number range covered by this segment.
    /// </summary>
    public (ulong Start, ulong End) SequenceRange { get; init; }

    /// <summary>
    ///     UTC timestamp when segment was created.
    /// </summary>
    public required DateTime CreatedAtUtc { get; init; }
}
