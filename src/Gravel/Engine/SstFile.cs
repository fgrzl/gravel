using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Engine;

public sealed record SstFile(string Path, ISstReader Reader, ulong SequenceTag)
{
    // Optional metadata for richer decisions (not yet populated):
    public ulong MaxSequence { get; init; }
}