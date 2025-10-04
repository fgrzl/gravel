using System.Collections.Concurrent;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Internals;
using Microsoft.Extensions.Options;
using Gravel.Abstractions;

namespace Gravel.Storage.InMemory.Sst;

public sealed class InMemorySstFactory : ISstFactory
{
    readonly ConcurrentDictionary<string, InMemorySst> _files;
    readonly InMemorySstOptions _options;

    public InMemorySstFactory(IOptions<InMemorySstOptions> options)
    {
        _options = options.Value;
        _files = new ConcurrentDictionary<string, InMemorySst>(
            _options.CaseInsensitivePaths ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public async ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return await Task.FromCanceled<ISstReader>(ct).ConfigureAwait(false);
        if (!_files.TryGetValue(path, out var sst)) throw new FileNotFoundException(path);

        var reader = new InMemorySstReader(sst) as ISstReader;
        if (reader is IAsyncInitializable ai)
            await ai.InitializeAsync(ct).ConfigureAwait(false);
        return reader;
    }

    public async ValueTask<ISstWriter> CreateWriterAsync(string path, int expectedEntries, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return await Task.FromCanceled<ISstWriter>(ct).ConfigureAwait(false);

        // Writer will seal into the factory on dispose/finish
        var writer = new InMemorySstWriter(sst => _files[path] = SealIfNeeded(sst)) as ISstWriter;
        if (writer is IAsyncInitializable ai)
            await ai.InitializeAsync(ct).ConfigureAwait(false);
        return writer;
    }

    public IEnumerable<string> EnumerateLevelFiles(string basePath, int level)
    {
        // In-memory factory doesn't use levels; return any keys that start with a 'basePath/L{level}' prefix if present
        var prefix = Path.Combine(basePath, $"L{level}");
        return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k);
    }

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

    public IReadOnlyCollection<string> ListPaths()
    {
        return _files.Keys.ToList();
    }
}
