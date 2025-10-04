namespace Gravel.Storage.InMemory.Sst;

/// <summary>
///     Options for the in-memory SST implementation (used for tests / embeddable scenarios).
/// </summary>
public sealed class InMemorySstOptions
{
    /// <summary>
    ///     Whether writers should deduplicate keys (keep only last value) before sealing.
    ///     Default true – mimics compaction behaviour.
    /// </summary>
    public bool DeduplicateOnSeal { get; set; } = true;

    /// <summary>
    ///     If true path keys are treated case-insensitive inside factory dictionary.
    /// </summary>
    public bool CaseInsensitivePaths { get; set; } = false;
}
