using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Storage.FileSystem.Wal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Benchmark.Storage.FileSystem.Wal;

[ShortRunJob]
[MemoryDiagnoser]
public class FileWalWriterBenchmarks
{
    string _dir = string.Empty;
    byte[][] _keys = null!;
    byte[][] _vals = null!;
    IWalWriter _writer = null!;

    // reduced N for quicker runs in CI/local debug
    [Params(1000)] public int N;

    [GlobalSetup]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gravel-bench-walw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _writer = new FileWalWriter(_dir, 8 * 1024 * 1024, NullLogger<FileWalWriter>.Instance);
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

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _writer.DisposeAsync();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
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