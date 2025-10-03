using System;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Gravel.Abstractions;
using Gravel.Storage.FileSystem.Wal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Benchmark.Storage.FileSystem.Wal;

[ShortRunJob]
[MemoryDiagnoser]
public class FileWalReaderBenchmarks
{
    string _dir = string.Empty;

    [Params(5000)] public int N;

    [GlobalSetup]
    public async Task Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gravel-bench-walr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        await using var w = new FileWalWriter(_dir, 64 * 1024, NullLogger<FileWalWriter>.Instance);
        for (ulong i = 0; i < (ulong)N; i++)
        {
            await w.BeginTransactionAsync(i);
            await w.AppendAsync(i, DbEntry.Put(BitConverter.GetBytes((long)i), BitConverter.GetBytes((long)i), 0));
            await w.CommitTransactionAsync(i);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    [Benchmark(Description = "Replay N small txns")]
    public async Task Replay_All()
    {
        await using var r = new FileWalReader(_dir, NullLogger<FileWalReader>.Instance);
        await foreach (var _ in r.ReplayAsync())
        {
        }
    }
}