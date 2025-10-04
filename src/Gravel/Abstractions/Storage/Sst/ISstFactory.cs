namespace Gravel.Abstractions.Storage.Sst;

public interface ISstFactory
{
    ISstReader CreateReader(string path);
    ISstWriter CreateWriter(string path, int expectedEntries);

    // Enumerate SST file paths for a given database base path and level (e.g., basePath/L{level}/*.sst).
    IEnumerable<string> EnumerateLevelFiles(string basePath, int level);
}
