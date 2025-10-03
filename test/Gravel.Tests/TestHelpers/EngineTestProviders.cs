using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Gravel.Compression;
using Gravel.Storage.FileSystem.Sst;
using Gravel.Storage.FileSystem.Wal;
using Gravel.Storage.InMemory.Sst;
using Gravel.Storage.InMemory.Wal;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Abstractions.Storage.Wal;

namespace Gravel.Tests.TestHelpers;

// Simple IOptionsMonitor wrapper for tests
sealed class SimpleOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    readonly T _value;
    public SimpleOptionsMonitor(T value) => _value = value;
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string> listener) => new DummyDisposable();
    sealed class DummyDisposable : IDisposable { public void Dispose() { } }
}

public static class EngineTestProviders
{
    public static IEnumerable<object[]> AllEngineProviders()
    {
        // In-memory providers
        var inSst = new InMemorySstFactory(Options.Create(new InMemorySstOptions()));
        var inWal = new InMemoryWalFactory(Options.Create(new InMemoryWalOptions()));
        yield return new object[] { "in-memory", inSst, inWal, null };

        // File-system providers (temp dir per run)
        var temp = Path.Combine(Path.GetTempPath(), "gravel-test-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        var fileSstOptions = new FileSstOptions { Path = temp, Kind = "file", SparseInterval = 128 };
        var sstOptionsMon = new SimpleOptionsMonitor<FileSstOptions>(fileSstOptions);
        var fileSstFactory = new FileSstFactory(sstOptionsMon, NullLoggerFactory.Instance, new CompressorFactory());

        var fileWalOptions = new FileWalOptions { Path = temp, WalSegmentSize = 64 * 1024 };
        var walOptionsMon = new SimpleOptionsMonitor<FileWalOptions>(fileWalOptions);
        var fileWalFactory = new FileWalFactory(walOptionsMon, NullLoggerFactory.Instance);

        yield return new object[] { "filesystem", fileSstFactory, fileWalFactory, temp };
    }
}
