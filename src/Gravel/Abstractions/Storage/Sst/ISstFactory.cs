namespace Gravel.Abstractions.Storage.Sst;

public interface ISstFactory
{
    // Enumerate SST file paths for a given database base path and level (e.g., basePath/L{level}/*.sst).
    IEnumerable<string> EnumerateLevelFiles(string basePath, int level);

    // Async-friendly factory method that implementations can override to eagerly initialize readers.
    // Default implementation provides compatibility by returning the synchronous CreateReader result.
    ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default);

    // Async-friendly factory method for creating writers. Implementations may override to perform
    // any required async initialization. Default implementation returns the synchronous CreateWriter result.
    ValueTask<ISstWriter> CreateWriterAsync(string path, int expectedEntries, CancellationToken ct = default);
}
