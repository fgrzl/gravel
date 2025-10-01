using System.Collections.Concurrent;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Internals;
using Microsoft.Extensions.Options;

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

    public ISstReader CreateReader(string path)
    {
        if (_files.TryGetValue(path, out var sst)) return new InMemorySstReader(sst);
        throw new FileNotFoundException(path);
    }

    public ISstWriter CreateWriter(string path, int expectedEntries)
    {
        return new InMemorySstWriter(sst => _files[path] = SealIfNeeded(sst));
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