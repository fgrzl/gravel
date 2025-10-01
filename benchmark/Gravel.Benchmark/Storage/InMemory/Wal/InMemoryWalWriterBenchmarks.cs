using System;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Storage.InMemory.Wal;

namespace Gravel.Benchmark.Storage.InMemory.Wal;

[ShortRunJob]
[MemoryDiagnoser]
public class InMemoryWalWriterBenchmarks
{
    byte[][] _keys = null!;
    byte[][] _vals = null!;
    InMemoryWalWriter _writer = null!;

    [Params(100_000)] public int N;

    [GlobalSetup]
    public void Setup()
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
        }
    }

    [Benchmark(Description = "Append N small puts (one txn each)")]
    public async Task Append_SingleTxnEach()
    {
        for (ulong i = 0; i < (ulong)N; i++)
        {
            await _writer.BeginTransactionAsync(i);
            await _writer.AppendAsync(i, DbEntry.Put(_keys[i], _vals[i], 0));
            await _writer.CommitTransactionAsync(i);
        }
    }

    [Benchmark(Description = "Append batch in single txn")]
    public async Task Append_SingleTxnBatch()
    {
        const ulong txn = 42;
        await _writer.BeginTransactionAsync(txn);
        for (var i = 0; i < N; i++)
            await _writer.AppendAsync(txn, DbEntry.Put(_keys[i], _vals[i], 0));
        await _writer.CommitTransactionAsync(txn);
    }
}