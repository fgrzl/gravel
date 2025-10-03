using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Compression;
using Gravel.Storage.FileSystem.Sst;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Benchmarks.Storage.FileSystem.Sst;

[ShortRunJob]
[MemoryDiagnoser]
public class FileSstReaderBenchmarks
{
    byte[][] _absentKeys = null!;
    byte[][] _keys = null!;
    string _path = string.Empty;

    [Params(10, 100, 1000)] public int N; // much smaller for dev/test

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravel-bench-sstr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "L0", "00000000000000000001.sst");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _keys = new byte[N][];
        _absentKeys = new byte[N][];
        var entries = new List<DbEntry>(N);
        for (var i = 0; i < N; i++)
        {
            var k = Encoding.UTF8.GetBytes($"k-{i:D8}");
            var v = Encoding.UTF8.GetBytes(new string('v', 16));
            _keys[i] = k;
            _absentKeys[i] = Encoding.UTF8.GetBytes($"z-{i:D8}"); // keys that are guaranteed to be absent
            entries.Add(DbEntry.Put(k, v, (ulong)(i + 1)));
        }

        var w = new FileSstWriter(_path, N, 64 * 1024, 16 * 1024, null, NullLogger<FileSstWriter>.Instance);

        async IAsyncEnumerable<DbEntry> Gen()
        {
            await Task.Yield();
            foreach (var e in entries) yield return e;
        }

        w.WriteAsync(Gen()).GetAwaiter().GetResult();
        w.DisposeAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(_path)!)!, true);
        }
        catch
        {
        }
    }

    [Benchmark(Description = "Point get hits (Bloom+Index)")]
    public async Task<long> Get_Hits()
    {
        using var r = new FileSstReader(_path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        long found = 0;
        for (var i = 0; i < N; i++)
        {
            // Use ValueTask for less allocation overhead
            var e = await r.GetAsync(_keys[i]).ConfigureAwait(false);
            if (e is { Kind: DbEntryKind.Put }) found++;
        }

        return found;
    }

    [Benchmark(Description = "Point get misses (Bloom+Index)")]
    public async Task<long> Get_Misses()
    {
        using var r = new FileSstReader(_path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        long notFound = 0;
        for (var i = 0; i < N; i++)
        {
            var e = await r.GetAsync(_absentKeys[i]).ConfigureAwait(false);
            if (e is null) notFound++;
        }

        return notFound;
    }

    [Benchmark(Description = "Full scan ReadAll")]
    public async Task<long> ReadAll_Full()
    {
        using var r = new FileSstReader(_path, new CompressorFactory(), NullLogger<FileSstReader>.Instance);
        long count = 0;
        await foreach (var _ in r.ReadAllAsync().ConfigureAwait(false))
            count++;
        return count;
    }
}