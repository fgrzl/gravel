using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Gravel.Engine;

namespace Gravel.Benchmarks.Engine;

[ShortRunJob]
[MemoryDiagnoser]
public class SkipListBenchmarks
{
    byte[][] _keys = null!;

    MemTable _mt = null!;
    byte[][] _values = null!;
    [Params(100_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        _mt = new MemTable();
        _keys = new byte[N][];
        _values = new byte[N][];
        var rnd = new Random(99);
        for (var i = 0; i < N; i++)
        {
            _keys[i] = Encoding.UTF8.GetBytes($"k-{i:D8}");
            var v = new byte[24];
            rnd.NextBytes(v);
            _values[i] = v;
        }
    }

    [IterationSetup(Target = nameof(Insert_Ascending))]
    public void Reset()
    {
        _mt = new MemTable();
    }

    [Benchmark(Description = "Insert ascending")]
    public void Insert_Ascending()
    {
        for (var i = 0; i < N; i++) _mt.Put(_keys[i], _values[i], (ulong)(i + 1));
    }

    [Benchmark(Description = "Insert descending")]
    public void Insert_Descending()
    {
        for (var i = N - 1; i >= 0; i--) _mt.Put(_keys[i], _values[i], (ulong)(N - i));
    }
}
