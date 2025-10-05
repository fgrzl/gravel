using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Engine;

namespace Gravel.Benchmarks.Engine;

[ShortRunJob]
[MemoryDiagnoser]
public class MemTableBenchmarks
{
    byte[][] _keys = null!;
    byte[][] _missKeys = null!;

    MemTable _mt = null!;
    byte[][] _values = null!;
    [Params(1_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        _keys = new byte[N][];
        _missKeys = new byte[N][];
        _values = new byte[N][];
        for (var i = 0; i < N; i++)
        {
            _keys[i] = Encoding.UTF8.GetBytes($"key-{i:D8}");
            _missKeys[i] = Encoding.UTF8.GetBytes($"zz-miss-{i:D8}");
            var val = new byte[64];
            rnd.NextBytes(val);
            _values[i] = val;
        }

        _mt = new MemTable();
        // preload for Get/Scan benchmarks
        for (var i = 0; i < N; i++) _mt.Put(_keys[i], _values[i], (ulong)(i + 1));
    }

    [IterationSetup(Target = nameof(Put_Sequential))]
    public void IterSetup_Put()
    {
        _mt = new MemTable();
    }

    [Benchmark(Description = "Put N sequential keys")]
    public void Put_Sequential()
    {
        for (var i = 0; i < N; i++) _mt.Put(_keys[i], _values[i], (ulong)(i + 1));
    }

    [Benchmark(Description = "Get hit N keys")]
    public int Get_Hit()
    {
        var hits = 0;
        for (var i = 0; i < N; i++)
            if (_mt.TryGet(_keys[i], out var val, out _, out var kind) && kind == DbEntryKind.Put && val.HasValue)
                hits++;

        return hits;
    }

    [Benchmark(Description = "Get miss N keys")]
    public int Get_Miss()
    {
        var misses = 0;
        for (var i = 0; i < N; i++)
            if (!_mt.TryGet(_missKeys[i], out _, out _, out _))
                misses++;

        return misses;
    }

    [Benchmark(Description = "Scan full table")]
    public int Scan_All()
    {
        var c = 0;
        foreach (var _ in _mt.Scan()) c++;
        return c;
    }

    [Benchmark(Description = "Range tombstones + Get")]
    public int RangeTombstone_MaskAndPut()
    {
        // apply a masking range, then re-put a few keys newer than the range
        var seqBase = (ulong)(N + 10);
        _mt.PutRangeTombstone("key-000100"u8, "key-000900"u8, seqBase);
        for (var i = 200; i < 300; i++) _mt.Put(_keys[i], _values[i], seqBase + (ulong)i);
        // probe some keys inside and outside range
        var found = 0;
        for (var i = 0; i < N; i += 137)
            if (_mt.TryGet(_keys[i], out var val, out _, out var kind) && kind == DbEntryKind.Put && val.HasValue)
                found++;
        return found;
    }
}
