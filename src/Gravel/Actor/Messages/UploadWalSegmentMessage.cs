namespace Gravel.Actor.Messages;

/// <summary>
///     Message signaling that a WAL segment should be uploaded to cloud storage.
///     The actor orchestrates this as a background task.
/// </summary>
public sealed class UploadWalSegmentMessage : ActorMessage
{
    /// <summary>
    ///     The local path to the WAL segment to be uploaded.
    /// </summary>
    public string LocalPath { get; init; } = default!;

    /// <summary>
    ///     The remote path/key in cloud storage where this segment should be stored.
    /// </summary>
    public string RemotePath { get; init; } = default!;

    /// <summary>
    ///     Size in bytes of the WAL segment (for bandwidth tracking).
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    ///     Gets the source of this message.
    /// </summary>
    public override string Source => "wal_upload";
}
