using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Engine;

/// <summary>
///     Represents an SST (Sorted String Table) file in the database, including its path, reader, and sequence tag.
/// </summary>
/// <param name="Path">The file path of the SST.</param>
/// <param name="Reader">The SST reader instance.</param>
/// <param name="SequenceTag">The sequence tag associated with this SST file.</param>
public sealed record SstFile(string Path, ISstReader Reader, ulong SequenceTag)
{
    /// <summary>
    ///     Optional metadata for richer decisions (not yet populated): the maximum sequence number in the SST file.
    /// </summary>
    public ulong MaxSequence { get; init; }
}
