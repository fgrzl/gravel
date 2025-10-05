namespace Gravel.Engine;

/// <summary>
///     Options for restoring a Gravel database from backup.
/// </summary>
public sealed class RestoreOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether to verify checksums during restore.
    /// </summary>
    public bool VerifyChecksums { get; set; } = true;
    /// <summary>
    ///     Gets or sets a value indicating whether the engine must be stopped before restore.
    /// </summary>
    public bool RequireEngineStopped { get; set; } = true;
}
