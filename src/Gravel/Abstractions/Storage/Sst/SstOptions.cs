namespace Gravel.Abstractions.Storage.Sst;

public abstract record SstOptions
{
    public required string Kind { get; init; }
    public required int SparseInterval { get; init; }
}