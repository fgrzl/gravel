namespace Gravel.Actor.Messages;

/// <summary>
///     Message signaling that the manifest (metadata about levels, files, sequences) should be synced
///     to persistent storage (local or cloud).
/// </summary>
public sealed class SyncManifestMessage : ActorMessage
{
    /// <summary>
    ///     Opaque manifest version/timestamp (set by the manifest builder).
    /// </summary>
    public string ManifestVersion { get; init; } = default!;

    /// <summary>
    ///     The serialized manifest data to be written.
    /// </summary>
    public byte[] ManifestData { get; init; } = [];

    /// <summary>
    ///     Gets the source of this message.
    /// </summary>
    public override string Source => "manifest_sync";
}
