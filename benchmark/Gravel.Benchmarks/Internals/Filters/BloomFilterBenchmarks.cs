using BenchmarkDotNet.Attributes;
using Gravel.Internals.Filters;

namespace Gravel.Benchmarks.Internals.Filters;

[ShortRunJob]
public class BloomFilterBenchmarks
{
    byte[] _existingKey = null!;
    BloomFilter _filter = null!;
    byte[] _missingKey = null!;

    [GlobalSetup]
    public void Setup()
    {
        _filter = BloomFilter.Create(10_000);
        _existingKey = "existing-key"u8.ToArray();
        _missingKey = "missing-key"u8.ToArray();
        _filter.Add(_existingKey);
    }

    [Benchmark]
    public bool MightContain_Hit()
    {
        return _filter.MightContain(_existingKey);
    }

    [Benchmark]
    public bool MightContain_Miss()
    {
        return _filter.MightContain(_missingKey);
    }
}
