using System;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Storage.InMemory.Sst;

namespace Gravel.Benchmarks.Storage.InMemory.Sst;

[ShortRunJob]
[MemoryDiagnoser]
public class InMemorySstWriterBenchmarks
{
    (ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)[] _entries = null!;

    [Params(100_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        _entries = new (ReadOnlyMemory<byte>, ReadOnlyMemory<byte>)[N];
        for (var i = 0; i < N; i++)
        {
            var k = Encoding.UTF8.GetBytes($"k-{i:D8}");
            var v = Encoding.UTF8.GetBytes(new string('v', 32));
            _entries[i] = (k, v);
        }
    }

    [Benchmark(Description = "Write N PUT entries")]
    public Task Write_All()
    {
        // Simulate async for consistency with other async benchmarks
        return Task.Run(() =>
        {
            var sst = new InMemorySst(_entries);
        });
    }
}
