using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gravel.Engine.Compaction;

// Simple in-memory fake compaction task for tests
class FakeCompactionTask : ICompactionTask
{
    readonly byte[][] _inputs;
    readonly List<byte> _output = new();
    int _idx;

    public FakeCompactionTask(string id, byte[][] inputs)
    {
        TaskId = id;
        _inputs = inputs;
        _idx = 0;
        TotalBytesExpected = _inputs.Sum(a => a.Length);
    }

    public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string TaskId { get; }
    public long TotalBytesExpected { get; }

    public Task PrepareAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<int> ReadNextInputAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (_idx >= _inputs.Length) return Task.FromResult(0);
        var src = _inputs[_idx++];
        src.AsSpan().CopyTo(buffer.Span);
        return Task.FromResult(src.Length);
    }

    public Task WriteOutputAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        _output.AddRange(buffer.ToArray());
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken ct)
    {
        Completion.TrySetResult(true);
        return Task.CompletedTask;
    }

    public byte[] GetOutputBytes()
    {
        return _output.ToArray();
    }
}