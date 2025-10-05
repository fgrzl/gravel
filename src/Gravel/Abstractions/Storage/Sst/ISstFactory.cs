namespace Gravel.Abstractions.Storage.Sst;

/// <summary>
///     Factory interface for creating SST readers and writers, and enumerating SST files by level.
/// </summary>
public interface ISstFactory
{
    /// <summary>
    ///     Enumerates SST file paths for a given database base path and level (e.g., basePath/L{level}/*.sst).
    /// </summary>
    /// <param name="basePath">The base path for the database.</param>
    /// <param name="level">The SST level.</param>
    /// <returns>An enumerable of SST file paths.</returns>
    IEnumerable<string> EnumerateLevelFiles(string basePath, int level);

    /// <summary>
    ///     Asynchronously creates an SST reader for the specified path.
    ///     Implementations can override to eagerly initialize readers.
    ///     Default implementation returns the synchronous CreateReader result.
    /// </summary>
    /// <param name="path">The path to the SST file.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous creation of an <see cref="ISstReader" />.</returns>
    ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Asynchronously creates an SST writer for the specified path and expected number of entries.
    ///     Implementations may override to perform async initialization.
    ///     Default implementation returns the synchronous CreateWriter result.
    /// </summary>
    /// <param name="path">The path to the SST file.</param>
    /// <param name="expectedEntries">The expected number of entries to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous creation of an <see cref="ISstWriter" />.</returns>
    ValueTask<ISstWriter> CreateWriterAsync(string path, int expectedEntries, CancellationToken ct = default);
}
