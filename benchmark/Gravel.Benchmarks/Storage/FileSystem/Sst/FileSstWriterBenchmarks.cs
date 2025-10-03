using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Storage.FileSystem.Sst;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Benchmarks.Storage.FileSystem.Sst;

[ShortRunJob]
[MemoryDiagnoser]
public class FileSstWriterBenchmarks
{
    (ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)[] _entries = null!;
    string _path = string.Empty;

    [Params(50_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravel-bench-sstw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "L0", "00000000000000000001.sst");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _entries = new (ReadOnlyMemory<byte>, ReadOnlyMemory<byte>)[N];
        for (var i = 0; i < N; i++)
        {
            var k = Encoding.UTF8.GetBytes($"k-{i:D8}");
            var v = Encoding.UTF8.GetBytes(new string('v', 32));
            _entries[i] = (k, v);
        }
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

    [Benchmark(Description = "Write N PUT entries")]
    public async Task Write_All()
    {
        await using var w = new FileSstWriter(_path, N, 64 * 1024, 16 * 1024, null, NullLogger<FileSstWriter>.Instance);
        await w.WriteAsync(GenAsync(_entries));
        return;

        static async IAsyncEnumerable<DbEntry> GenAsync(
            (ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)[] entries)
        {
            foreach (var (k, v) in entries)
            {
                yield return DbEntry.Put(k, v, 0);
                await Task.CompletedTask; // minimal async overhead, no context switch
            }
        }
    }
}