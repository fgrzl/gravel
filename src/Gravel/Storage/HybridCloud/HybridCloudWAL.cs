using Gravel.Actor;
using Gravel.Actor.Messages;
using Gravel.Cloud.Abstractions;
using Gravel.Storage.TLV;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Gravel.Storage.HybridCloud;

/// <summary>
///     Hybrid cloud WAL: fast local buffer, async cloud durability.
///     - Appends go to memory buffer (fast write path)
///     - Segments roll and are queued for cloud upload via actor
///     - Recovery reads from cloud (source of truth)
/// </summary>
public sealed class HybridCloudWAL : IAsyncDisposable
{
    readonly IActorRuntime _runtime;
    readonly ICloudStorage _cloudStorage;
    readonly string _localCacheDir;
    readonly int _segmentSizeBytes;
    readonly ILogger _logger;

    ulong _currentSequence;
    byte[] _buffer;
    int _bufferOffset;
    readonly object _bufferLock = new();

    List<HybridWalSegment> _localSegments = [];

    /// <summary>
    ///     Initializes a new instance of <see cref="HybridCloudWAL" />.
    /// </summary>
    public HybridCloudWAL(
        IActorRuntime runtime,
        ICloudStorage cloudStorage,
        string localCacheDir,
        int segmentSizeBytes = 10 * 1024 * 1024,
        ILogger? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _cloudStorage = cloudStorage ?? throw new ArgumentNullException(nameof(cloudStorage));
        _localCacheDir = localCacheDir ?? throw new ArgumentNullException(nameof(localCacheDir));
        _segmentSizeBytes = segmentSizeBytes;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        Directory.CreateDirectory(_localCacheDir);
        _buffer = new byte[segmentSizeBytes];
        _bufferOffset = 0;
    }

    /// <summary>
    ///     Gets the last durable sequence number.
    /// </summary>
    public ulong LastDurableSequence => _currentSequence;

    /// <summary>
    ///     Appends entry to buffer (fast, in-memory).
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

            // Auto-roll when segment full
            if (_bufferOffset + needed > _segmentSizeBytes)
            {
                QueueSegmentForUpload();
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
    ///     Flushes current buffer to local cache and queues upload.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken ct = default)
    {
        lock (_bufferLock)
        {
            if (_bufferOffset > 0)
            {
                QueueSegmentForUpload();
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Rolls segment and posts upload message to actor.
    /// </summary>
    private void QueueSegmentForUpload()
    {
        if (_bufferOffset == 0)
            return;

        var segmentId = $"wal-{DateTime.UtcNow.Ticks:D20}";
        var localPath = Path.Combine(_localCacheDir, segmentId + ".seg");

        // Write to local cache
        File.WriteAllBytes(localPath, _buffer[.._bufferOffset]);

        var segment = new HybridWalSegment
        {
            SegmentId = segmentId,
            LocalPath = localPath,
            CloudPath = $"wal/segments/{segmentId}",
            Size = _bufferOffset,
            SequenceRange = (_currentSequence - (ulong)_bufferOffset, _currentSequence),
            CreatedAtUtc = DateTime.UtcNow
        };

        _localSegments.Add(segment);

        // Post upload message to actor runtime
        var uploadMsg = new UploadWalSegmentMessage
        {
            LocalPath = localPath,
            RemotePath = segment.CloudPath,
            SizeBytes = _bufferOffset
        };

        // Fire and forget - actor will handle retries
        _ = _runtime.PostMessageAsync(uploadMsg, CancellationToken.None);

        _logger.LogDebug("Queued WAL segment upload: {SegmentId}, {Size} bytes", segmentId, _bufferOffset);

        // Reset buffer
        _buffer = new byte[_segmentSizeBytes];
        _bufferOffset = 0;
    }

    /// <summary>
    ///     Recovers entries from cloud (source of truth).
    /// </summary>
    public async IAsyncEnumerable<Abstractions.DbEntry> RecoverAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        // List all segments from cloud
        var cloudSegments = new List<string>();
        await foreach (var path in _cloudStorage.ListAsync("wal/segments/", ct).ConfigureAwait(false))
        {
            cloudSegments.Add(path);
        }

        // Process in order
        foreach (var segmentPath in cloudSegments.OrderBy(s => s))
        {
            using var ms = new MemoryStream();
            await _cloudStorage.DownloadAsync(segmentPath, ms, ct).ConfigureAwait(false);

            var data = ms.ToArray();
            var reader = new TLVReader(data);

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

            _currentSequence++;
        }
    }

    /// <summary>
    ///     Disposes the WAL.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);

        // Clean up local cache after confirming cloud has data
        try
        {
            foreach (var seg in _localSegments)
            {
                if (seg.IsCloudDurable && File.Exists(seg.LocalPath))
                {
                    File.Delete(seg.LocalPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up local WAL cache");
        }
    }
}

/// <summary>
///     Hybrid WAL segment metadata.
/// </summary>
public sealed class HybridWalSegment
{
    /// <summary>
    ///     Segment identifier.
    /// </summary>
    public required string SegmentId { get; init; }

    /// <summary>
    ///     Local file path (ephemeral cache).
    /// </summary>
    public required string LocalPath { get; init; }

    /// <summary>
    ///     Cloud object path (source of truth).
    /// </summary>
    public required string CloudPath { get; init; }

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
