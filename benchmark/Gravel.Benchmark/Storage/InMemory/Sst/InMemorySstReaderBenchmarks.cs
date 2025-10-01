using System;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Storage.InMemory.Sst;

namespace Gravel.Benchmark.Storage.InMemory.Sst;

[ShortRunJob]
[MemoryDiagnoser]
public class InMemorySstReaderBenchmarks
{
    byte[][] _keys = null!;
    InMemorySst _sst = null!;

    [Params(100_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        _keys = new byte[N][];
        var entries = new (ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)[N];
        for (var i = 0; i < N; i++)
        {
            var k = Encoding.UTF8.GetBytes($"k-{i:D8}");
            var v = Encoding.UTF8.GetBytes(new string('v', 16));
            _keys[i] = k;
            entries[i] = (k, v);
        }

        _sst = new InMemorySst(entries);
    }

    [Benchmark(Description = "Point get hits")]
    public Task<int> Get_Hits()
    {
        // Simulate async for consistency with other async benchmarks
        return Task.Run(() =>
        {
            var found = 0;
            for (var i = 0; i < N; i++)
                if (_sst.TryGet(_keys[i], out var v) && v != null)
                    found++;
            return found;
        });
    }

    [Benchmark(Description = "Full scan")]
    public Task<int> Scan_All()
    {
        return Task.Run(() =>
        {
            var count = 0;
            foreach (var (k, v) in _sst.Entries) count++;
            return count;
        });
    }
}