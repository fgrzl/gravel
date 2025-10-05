namespace Gravel.Abstractions.Storage.Sst;

/// <summary>
///     Specifies the compression algorithm used for SST blocks.
/// </summary>
public enum CompressionKind : byte
{
    /// <summary>
    ///     No compression.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Snappy compression.
    /// </summary>
    Snappy = 1
}
