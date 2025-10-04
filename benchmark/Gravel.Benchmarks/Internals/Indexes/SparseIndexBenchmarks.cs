using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Gravel.Internals.Indexes;

namespace Gravel.Benchmarks.Internals.Indexes;

[ShortRunJob]
[MemoryDiagnoser]
public class SparseIndexBenchmarks
{
    SparseIndex _idx = null!;
    byte[][] _keys = null!;
    [Params(10_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        _idx = new SparseIndex();
        _keys = new byte[N][];
        for (var i = 0; i < N; i++)
        {
            _keys[i] = Encoding.UTF8.GetBytes($"key-{i:D8}");
            _idx.AddSample(_keys[i], i * 128);
        }
    }

    [Benchmark(Description = "FindFloor existing keys")]
    public long FindFloor_Existing()
    {
        long sum = 0;
        for (var i = 0; i < N; i++) sum += _idx.FindFloor(_keys[i]);
        return sum;
    }

    [Benchmark(Description = "FindFloor between samples")]
    public long FindFloor_Between()
    {
        long sum = 0;
        // reuse a single buffer sized to hold the key plus "-x" to avoid allocations per iteration
        var probeBuf = new byte[_keys[0].Length + 2];
        for (var i = 0; i < N; i++)
        {
            // copy base key bytes and append '-' and 'x'
            var baseKey = _keys[i];
            Array.Copy(baseKey, 0, probeBuf, 0, baseKey.Length);
            probeBuf[baseKey.Length] = (byte)'-';
            probeBuf[baseKey.Length + 1] = (byte)'x';
            sum += _idx.FindFloor(probeBuf);
        }

        return sum;
    }
}
