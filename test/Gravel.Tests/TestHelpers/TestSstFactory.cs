using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;

namespace Gravel.TestHelpers;

// Test-only SST factory that serves pre-registered readers per (basePath, level)
sealed class TestSstFactory : ISstFactory
{
    readonly Dictionary<string, ISstReader> _readers = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return await Task.FromCanceled<ISstReader>(ct).ConfigureAwait(false);

        if (!_readers.TryGetValue(path, out var r))
            throw new FileNotFoundException(path);

        if (r is IAsyncInitializable ai)
            await ai.InitializeAsync(ct).ConfigureAwait(false);

        return r;
    }

    public ValueTask<ISstWriter> CreateWriterAsync(string path, int expectedEntries, CancellationToken ct = default)
    {
        // Test factory doesn't support writers; keep behavior consistent by throwing
        throw new NotSupportedException("Test factory writer not supported in this test");
    }

    public IEnumerable<string> EnumerateLevelFiles(string basePath, int level)
    {
        var prefix = Path.Combine(basePath, $"L{level}") + Path.DirectorySeparatorChar;
        return _readers.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k);
    }

    public void Register(string basePath, int level, string fileName, IEnumerable<DbEntry> entries)
    {
        var path = Path.Combine(basePath, $"L{level}", fileName);
        _readers[path] = new TestSstReader(entries);
    }
}
