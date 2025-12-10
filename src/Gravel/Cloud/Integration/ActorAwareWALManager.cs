using Gravel.Abstractions;
using Gravel.Actor;
using Gravel.Actor.Messages;
using Gravel.Cloud.WAL;
using Microsoft.Extensions.Logging;

namespace Gravel.Cloud.Integration;

/// <summary>
///     Bridges the actor runtime with cloud-native WAL.
///     Posts messages to actor for async uploads while keeping write path fast.
/// </summary>
public sealed class ActorAwareWALManager : IAsyncDisposable
{
    readonly CloudNativeWAL _wal;
    readonly IActorRuntime _runtime;
    readonly ILogger _logger;

    /// <summary>
    ///     Initializes a new instance of <see cref="ActorAwareWALManager" />.
    /// </summary>
    public ActorAwareWALManager(IActorRuntime runtime, ILogger? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _wal = new CloudNativeWAL(logger: logger);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>
    ///     Gets the last durable sequence number.
    /// </summary>
    public ulong LastDurableSequence => _wal.LastDurableSequence;

    /// <summary>
    ///     Appends entry and posts upload message if segment rolls.
    /// </summary>
    public async ValueTask AppendAsync(DbEntry entry, CancellationToken ct = default)
    {
        var prevSegmentCount = _wal.GetSegments().Count;
        await _wal.AppendAsync(entry, ct).ConfigureAwait(false);
        var newSegmentCount = _wal.GetSegments().Count;

        if (newSegmentCount > prevSegmentCount)
        {
            // Segment was rolled, queue upload to actor
            var segments = _wal.GetSegments();
            var rolledSegment = segments[^2]; // Previous segment (now completed)

            var uploadMsg = new UploadWalSegmentMessage
            {
                LocalPath = $"wal/{rolledSegment.SegmentId}",
                RemotePath = $"wal/segments/{rolledSegment.SegmentId}",
                SizeBytes = rolledSegment.Size
            };

            await _runtime.PostMessageAsync(uploadMsg, ct).ConfigureAwait(false);
            _logger.LogDebug("Queued WAL segment upload: {SegmentId}", rolledSegment.SegmentId);
        }
    }

    /// <summary>
    ///     Recovers entries from all segments.
    /// </summary>
    public async IAsyncEnumerable<DbEntry> RecoverAsync()
    {
        var segments = _wal.GetSegments();
        await foreach (var entry in _wal.RecoverAsync(segments))
        {
            yield return entry;
        }
    }

    /// <summary>
    ///     Disposes the manager.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _wal.DisposeAsync().ConfigureAwait(false);
    }
}
