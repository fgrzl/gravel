using System.Collections.Concurrent;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Internals;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.InMemory.Sst;

/// <summary>
///     In-memory SST factory for creating readers and writers, managing SST files in a concurrent dictionary.
///     Supports case-insensitive paths and deduplication on seal based on options.
/// </summary>
public sealed class InMemorySstFactory : ISstFactory
{
    readonly ConcurrentDictionary<string, InMemorySst> _files;
    readonly InMemorySstOptions _options;

    /// <summary>
    ///     Initializes a new instance of <see cref="InMemorySstFactory" />.
    /// </summary>
    /// <param name="options">The in-memory SST options.</param>
    public InMemorySstFactory(IOptions<InMemorySstOptions> options)
    {
        _options = options.Value;
        _files = new ConcurrentDictionary<string, InMemorySst>(
            _options.CaseInsensitivePaths ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <summary>
    ///     Asynchronously creates an SST reader for the specified path.
    /// </summary>
    /// <param name="path">The path to the SST file.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous creation of an <see cref="ISstReader" />.</returns>
    public async ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return await Task.FromCanceled<ISstReader>(ct).ConfigureAwait(false);
        if (!_files.TryGetValue(path, out var sst)) throw new FileNotFoundException(path);

        var reader = new InMemorySstReader(sst) as ISstReader;
        if (reader is IAsyncInitializable ai)
            await ai.InitializeAsync(ct).ConfigureAwait(false);
        return reader;
    }

    /// <summary>
    ///     Asynchronously creates an SST writer for the specified path and expected number of entries.
    /// </summary>
    /// <param name="path">The path to the SST file.</param>
    /// <param name="expectedEntries">The expected number of entries to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous creation of an <see cref="ISstWriter" />.</returns>
    public async ValueTask<ISstWriter> CreateWriterAsync(
        string path, int expectedEntries, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return await Task.FromCanceled<ISstWriter>(ct).ConfigureAwait(false);

        // Writer will seal into the factory on dispose/finish
        var writer = new InMemorySstWriter(sst => _files[path] = SealIfNeeded(sst)) as ISstWriter;
        if (writer is IAsyncInitializable ai)
            await ai.InitializeAsync(ct).ConfigureAwait(false);
        return writer;
    }

    /// <summary>
    ///     Enumerates SST file paths for a given database base path and level.
    /// </summary>
    /// <param name="basePath">The base path for the database.</param>
    /// <param name="level">The SST level.</param>
    /// <returns>An enumerable of SST file paths.</returns>
    public IEnumerable<string> EnumerateLevelFiles(string basePath, int level)
    {
        // In-memory factory doesn't use levels; return any keys that start with a 'basePath/L{level}' prefix if present
        var prefix = Path.Combine(basePath, $"L{level}");
        return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k);
    }

    /// <summary>
    ///     Seals an in-memory SST, deduplicating keys if configured.
    /// </summary>
    /// <param name="sst">The in-memory SST to seal.</param>
    /// <returns>The sealed <see cref="InMemorySst" />.</returns>
    InMemorySst SealIfNeeded(InMemorySst sst)
    {
        if (!_options.DeduplicateOnSeal) return sst;
        // Keep last value per key (simulate basic compaction output semantics)
        var map = new Dictionary<string, (byte[] Key, byte[] Value)>(sst.Entries.Length,
            _options.CaseInsensitivePaths ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var (k, v) in sst.Entries) map[Convert.ToBase64String(k)] = (k, v);
        var dedup = map.Values.OrderBy(e => e.Key, Comparer<byte[]>.Create(ByteComparer.Compare));
        return new InMemorySst(dedup.Select(e => ((ReadOnlyMemory<byte>)e.Key, (ReadOnlyMemory<byte>)e.Value)));
    }

    /// <summary>
    ///     Lists all SST file paths managed by this factory.
    /// </summary>
    /// <returns>A read-only collection of SST file paths.</returns>
    public IReadOnlyCollection<string> ListPaths()
    {
        return _files.Keys.ToList();
    }
}
