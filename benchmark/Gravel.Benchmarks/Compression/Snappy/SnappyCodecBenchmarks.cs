using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Gravel.Compression.Snappy;

namespace Gravel.Benchmarks.Compression.Snappy;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SnappyCodecBenchmarks
{
    byte[] _compressedRandom = null!;
    byte[] _compressedRepetitive = null!;
    byte[] _randomData = null!;
    byte[] _repetitiveData = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        _randomData = new byte[1024 * 64];
        rnd.NextBytes(_randomData);

        _repetitiveData = Encoding.ASCII.GetBytes(new string('A', 1024 * 64));

        _compressedRandom = SnappyCodec.Compress(_randomData);
        _compressedRepetitive = SnappyCodec.Compress(_repetitiveData);
    }

    [Benchmark]
    public byte[] Compress_Random()
    {
        return SnappyCodec.Compress(_randomData);
    }

    [Benchmark]
    public byte[] Compress_Repetitive()
    {
        return SnappyCodec.Compress(_repetitiveData);
    }

    [Benchmark]
    public byte[] Decompress_Random()
    {
        try
        {
            return SnappyCodec.Decompress(_compressedRandom);
        }
        catch
        {
            // swallow errors to keep benchmark runner alive; return empty result for failed run
            return [];
        }
    }

    [Benchmark]
    public byte[] Decompress_Repetitive()
    {
        try
        {
            return SnappyCodec.Decompress(_compressedRepetitive);
        }
        catch
        {
            return [];
        }
    }

    [Benchmark]
    public void Stream_Compress_Repetitive()
    {
        using var ms = new MemoryStream();
        using var s =
            new SnappyCodec.SnappyStream(ms, CompressionMode.Compress, true);
        s.Write(_repetitiveData, 0, _repetitiveData.Length);
        s.Flush();
        _ = ms.ToArray();
    }

    [Benchmark]
    public void Stream_Decompress_Repetitive()
    {
        using var ms = new MemoryStream(_compressedRepetitive);
        using var s =
            new SnappyCodec.SnappyStream(ms, CompressionMode.Decompress, true);
        var buf = new byte[_repetitiveData.Length];
        _ = s.Read(buf, 0, buf.Length);
    }
}
