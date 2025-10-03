using System;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Gravel.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class LexKeyBenchmarks
{
    LexKey _comparer;
    string[] _hexes = null!;
    LexKey[] _keys = null!;
    object[][] _partsArrays = null!;
    string[] _strings = null!;

    [Params(3)] public int N;
    [Params(16)] public int StringSize;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        _strings = new string[N];
        _partsArrays = new object[N][];
        _keys = new LexKey[N];
        _hexes = new string[N];

        for (var i = 0; i < N; i++)
        {
            var sb = new StringBuilder(StringSize);
            for (var j = 0; j < StringSize; j++)
                sb.Append((char)('a' + rnd.Next(26)));
            _strings[i] = sb.ToString();

            var b = new byte[8];
            rnd.NextBytes(b);

            var parts = new object[] { _strings[i], i, GuidFromSeed(i), b };
            _partsArrays[i] = parts;

            var key = LexKey.New(parts);
            _keys[i] = key;
            _hexes[i] = key.ToHexString();
        }

        _comparer = LexKey.Empty;
    }

    static Guid GuidFromSeed(int i)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(i).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    [Benchmark(Description = "Encode - Single String")]
    public LexKey Encode_SingleString()
    {
        return LexKey.Encode(_strings[0]);
    }

    [Benchmark(Description = "Encode - Mixed Parts")]
    public LexKey Encode_MixedParts()
    {
        return LexKey.New(_partsArrays[0]);
    }

    [Benchmark(Description = "ToHexString")]
    public string ToHexString()
    {
        return _keys[0].ToHexString();
    }

    [Benchmark(Description = "FromHexString")]
    public LexKey FromHexString()
    {
        return LexKey.FromHexString(_hexes[0]);
    }

    [Benchmark(Description = "GetHashCode")]
    public int GetHashCode_Bench()
    {
        return _keys[0].GetHashCode();
    }

    [Benchmark(Description = "Equals - Same Ref")]
    public bool Equals_SameRef()
    {
        return _keys[0].Equals(_keys[0]);
    }

    [Benchmark(Description = "Equals - Structurally Equal")]
    public bool Equals_Structural()
    {
        var clone = LexKey.New(_partsArrays[0]);
        return _keys[0].Equals(clone);
    }

    [Benchmark(Description = "Equals - Different")]
    public bool Equals_Different()
    {
        return _keys[0].Equals(_keys[1]);
    }

    [Benchmark(Description = "Compare - Same Ref")]
    public int Compare_SameRef()
    {
        return _comparer.Compare(_keys[0], _keys[0]);
    }

    [Benchmark(Description = "Compare - Different")]
    public int Compare_Different()
    {
        return _comparer.Compare(_keys[0], _keys[1]);
    }
}