using System;
using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using Gravel.Storage.Shared;

namespace Gravel.Benchmarks.Storage.Shared;

[SimpleJob]
[MemoryDiagnoser]
public class StorageBlockBuildersBenchmarks
{
    byte[][] _keys = null!;
    byte[][] _values = null!;

    [Params(8, 64)] public int KeyLen;

    [Params(1_000, 10_000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        _keys = new byte[N][];
        _values = new byte[N][];
        for (var i = 0; i < N; i++)
        {
            // create keys with a moderate shared prefix to exercise prefix compression
            var prefix = "prefix-"u8.ToArray();
            var suffix = Encoding.UTF8.GetBytes(i.ToString("D6"));
            var key = new byte[prefix.Length + suffix.Length + Math.Max(0, KeyLen - prefix.Length - suffix.Length)];
            prefix.CopyTo(key, 0);
            suffix.CopyTo(key, prefix.Length);
            // fill remainder with deterministic bytes
            for (var j = prefix.Length + suffix.Length; j < key.Length; j++) key[j] = (byte)(j & 0xff);
            _keys[i] = key;

            var val = new byte[64];
            rnd.NextBytes(val);
            _values[i] = val;
        }
    }

    // ----------------------- DataBlockBuilder -----------------------
    [Benchmark]
    public void DataBlockBuilder_AddAll()
    {
        var b = new DataBlockBuilder(16);
        for (var i = 0; i < N; i++)
            b.Add(_keys[i], _values[i]);
    }

    [Benchmark]
    public void DataBlockBuilder_AddAndFinishPooled()
    {
        var b = new DataBlockBuilder(16);
        for (var i = 0; i < N; i++)
            b.Add(_keys[i], _values[i]);

        var buf = b.FinishPooled();
        // simulate consumer returning the buffer
        ArrayPool<byte>.Shared.Return(buf.Buffer);
    }

    // ----------------------- SimpleBlockBuilder -----------------------
    [Benchmark]
    public void SimpleBlockBuilder_AddAll()
    {
        var b = new SimpleBlockBuilder();
        for (var i = 0; i < N; i++)
            b.Add(_keys[i], new BlockHandle((ulong)i * 100UL, 128UL));
    }

    [Benchmark]
    public void SimpleBlockBuilder_AddAndFinish()
    {
        var b = new SimpleBlockBuilder();
        for (var i = 0; i < N; i++)
            b.Add(_keys[i], new BlockHandle((ulong)i * 100UL, 128UL));

        var arr = b.Finish();
        // touch result to avoid optimizing away
        if (arr.Length == 0) throw new Exception("unexpected");
    }

    // ----------------------- FullFilterBlockBuilder -----------------------
    [Benchmark]
    public void FullFilterBlockBuilder_AddAll()
    {
        var b = new FullFilterBlockBuilder(N);
        for (var i = 0; i < N; i++)
            b.AddKey(_keys[i]);
    }

    [Benchmark]
    public void FullFilterBlockBuilder_AddAndFinish()
    {
        var b = new FullFilterBlockBuilder(N);
        for (var i = 0; i < N; i++)
            b.AddKey(_keys[i]);

        var arr = b.Finish();
        if (arr.Length == 0) throw new Exception("unexpected");
    }

    // ----------------------- RangeDeleteBlockBuilder -----------------------
    [Benchmark]
    public void RangeDeleteBlockBuilder_AddAll()
    {
        var b = new RangeDeleteBlockBuilder();
        for (var i = 0; i < N; i++)
            b.Add(_keys[i], _keys[Math.Min(N - 1, i + 1)], (ulong)i + 1);
    }

    [Benchmark]
    public void RangeDeleteBlockBuilder_AddAndFinish()
    {
        var b = new RangeDeleteBlockBuilder();
        for (var i = 0; i < N; i++)
            b.Add(_keys[i], _keys[Math.Min(N - 1, i + 1)], (ulong)i + 1);

        var arr = b.Finish();
        // ensure arr consumed
        if (arr.Length < 0) throw new Exception("impossible");
    }
}
