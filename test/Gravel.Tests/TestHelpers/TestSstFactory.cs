using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;

namespace Gravel.TestHelpers;

// Test-only SST factory that serves pre-registered readers per (basePath, level)
sealed class TestSstFactory : ISstFactory
{
    readonly Dictionary<string, ISstReader> _readers = new(StringComparer.OrdinalIgnoreCase);

    public ISstReader CreateReader(string path)
    {
        if (_readers.TryGetValue(path, out var r)) return r;
        throw new FileNotFoundException(path);
    }

    public ISstWriter CreateWriter(string path, int expectedEntries)
    {
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
