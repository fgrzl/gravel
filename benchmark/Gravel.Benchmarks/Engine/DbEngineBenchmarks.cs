using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel;
using Gravel.Abstractions;

namespace Gravel.Benchmarks.Engine;

[ShortRunJob]
[MemoryDiagnoser]
public class DbEngineBenchmarks
{
    IDbEngine _engine = null!;
    byte[][] _keys = null!;
    byte[][] _vals = null!;

    [Params(10_000)] public int N;
    [Params(32)] public int ValueSize;
    [Params(128)] public int BatchSize;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Create an in-memory engine and prepare keys/values
        _engine = GravelFactory.CreateInMemoryAsync().GetAwaiter().GetResult();
        var rnd = new Random(42);
        _keys = new byte[N][];
        _vals = new byte[N][];
        for (var i = 0; i < N; i++)
        {
            _keys[i] = Encoding.UTF8.GetBytes($"k-{i:D10}");
            var v = new byte[ValueSize];
            rnd.NextBytes(v);
            _vals[i] = v;
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        try
        {
            _engine.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    [IterationSetup(Target = nameof(Put_Sequential))]
    public void Setup_Put()
    {
        // fresh engine for put benchmark
        _engine?.DisposeAsync().GetAwaiter().GetResult();
        _engine = GravelFactory.CreateInMemoryAsync().GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Put sequential small values")]
    public async Task Put_Sequential()
    {
        for (var i = 0; i < N; i++)
            await _engine.PutAsync(_keys[i], _vals[i]).ConfigureAwait(false);
    }

    [IterationSetup(Target = nameof(Get_Hits))]
    public void Setup_Get()
    {
        _engine?.DisposeAsync().GetAwaiter().GetResult();
        _engine = GravelFactory.CreateInMemoryAsync().GetAwaiter().GetResult();
        // prepopulate
        for (var i = 0; i < N; i++)
            _engine.PutAsync(_keys[i], _vals[i]).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Get point hits")]
    public async Task<int> Get_Hits()
    {
        var found = 0;
        for (var i = 0; i < N; i++)
        {
            var v = await _engine.GetAsync(_keys[i]).ConfigureAwait(false);
            if (v.HasValue) found++;
        }

        return found;
    }

    [IterationSetup(Target = nameof(Batch_Mutations))]
    public void Setup_Batch()
    {
        _engine?.DisposeAsync().GetAwaiter().GetResult();
        _engine = GravelFactory.CreateInMemoryAsync().GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Batch mutations")]
    public async Task Batch_Mutations()
    {
        var batch = new List<Mutation>(BatchSize);
        for (var i = 0; i < N; i++)
        {
            batch.Add(Mutation.Put(_keys[i], _vals[i]));
            if (batch.Count >= BatchSize)
            {
                await _engine.BatchAsync(batch).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0) await _engine.BatchAsync(batch).ConfigureAwait(false);
    }

    [IterationSetup(Target = nameof(Scan_Full))]
    public void Setup_Scan()
    {
        _engine?.DisposeAsync().GetAwaiter().GetResult();
        _engine = GravelFactory.CreateInMemoryAsync().GetAwaiter().GetResult();
        for (var i = 0; i < N; i++)
            _engine.PutAsync(_keys[i], _vals[i]).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Full scan")]
    public async Task<int> Scan_Full()
    {
        var count = 0;
        await foreach (var _ in _engine.ScanAsync(new Query(null)).ConfigureAwait(false))
            count++;
        return count;
    }

    [IterationSetup(Target = nameof(Transaction_Commit))]
    public void Setup_Txn()
    {
        _engine?.DisposeAsync().GetAwaiter().GetResult();
        _engine = GravelFactory.CreateInMemoryAsync().GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Transaction commit multiple ops")]
    public async Task Transaction_Commit()
    {
        var batchSize = Math.Max(1, BatchSize);
        var idx = 0;
        while (idx < N)
        {
            var tx = await _engine.BeginTransactionAsync().ConfigureAwait(false);
            for (var j = 0; j < batchSize && idx < N; j++, idx++)
                await tx.PutAsync(_keys[idx], _vals[idx]).ConfigureAwait(false);
            await tx.CommitAsync().ConfigureAwait(false);
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    [IterationSetup(Target = nameof(Concurrent_Puts))]
    public void Setup_Concurrent()
    {
        _engine?.DisposeAsync().GetAwaiter().GetResult();
        _engine = GravelFactory.CreateInMemoryAsync().GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Concurrent puts (Task.WhenAll)")]
    public async Task Concurrent_Puts()
    {
        var tasks = new List<Task>(Environment.ProcessorCount);
        var perTask = N / Math.Max(1, Environment.ProcessorCount);
        for (var t = 0; t < Environment.ProcessorCount; t++)
        {
            var start = t * perTask;
            var end = Math.Min(N, start + perTask);
            tasks.Add(Task.Run(async () =>
            {
                for (var i = start; i < end; i++)
                    await _engine.PutAsync(_keys[i], _vals[i]).ConfigureAwait(false);
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
