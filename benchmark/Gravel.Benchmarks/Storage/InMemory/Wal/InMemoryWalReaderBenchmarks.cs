using System;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Storage.InMemory.Wal;

namespace Gravel.Benchmarks.Storage.InMemory.Wal;

[ShortRunJob]
[MemoryDiagnoser]
public class InMemoryWalReaderBenchmarks
{
    byte[][] _keys = null!;
    byte[][] _vals = null!;
    InMemoryWalWriter _writer = null!;

    [Params(100_000)] public int N;

    [GlobalSetup]
    public async Task Setup()
    {
        _writer = new InMemoryWalWriter(N * 2);
        _keys = new byte[N][];
        _vals = new byte[N][];
        var rnd = new Random(7);
        for (var i = 0; i < N; i++)
        {
            _keys[i] = Encoding.UTF8.GetBytes($"k-{i:D8}");
            var v = new byte[64];
            rnd.NextBytes(v);
            _vals[i] = v;
            await _writer.BeginTransactionAsync((ulong)i);
            await _writer.AppendAsync((ulong)i, DbEntry.Put(_keys[i], _vals[i], 0));
            await _writer.CommitTransactionAsync((ulong)i);
        }
    }

    [Benchmark(Description = "Replay all txns")]
    public async Task<int> Replay_All()
    {
        var reader = new InMemoryWalReader(_writer);
        var count = 0;
        await foreach (var _ in reader.ReplayAsync())
            count++;
        return count;
    }
}
