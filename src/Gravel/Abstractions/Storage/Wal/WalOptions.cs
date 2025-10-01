namespace Gravel.Abstractions.Storage.Wal;

public abstract record WalOptions
{
    public required string Kind { get; init; }

    public required string SegmentSize { get; set; }
}