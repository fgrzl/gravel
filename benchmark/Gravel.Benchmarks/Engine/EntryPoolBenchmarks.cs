using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Engine;

namespace Gravel.Benchmark.Engine;

[ShortRunJob]
[MemoryDiagnoser]
public class EntryPoolBenchmarks
{
    byte[][] _keys = null!;

    EntryPool _pool = null!;
    byte[][] _values = null!;
    [Params(10_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        _pool = new EntryPool();
        _keys = new byte[N][];
        _values = new byte[N][];
        var rnd = new Random(123);
        for (var i = 0; i < N; i++)
        {
            _keys[i] = Encoding.UTF8.GetBytes($"key-{i:D8}");
            var v = new byte[32];
            rnd.NextBytes(v);
            _values[i] = v;
        }
    }

    [Benchmark(Description = "Rent + Return objects only")]
    public int RentReturn_Objects()
    {
        var arr = new EntryPool.Entry[N];
        for (var i = 0; i < N; i++)
            arr[i] = _pool.Rent(_keys[i], _keys[i].Length, _values[i], _values[i].Length, (ulong)i, DbEntryKind.Put);
        for (var i = 0; i < N; i++) _pool.Return(arr[i]);
        return arr.Length;
    }
}