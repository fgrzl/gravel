using System.Buffers.Binary;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression.Default;
using Gravel.Internals;
using Gravel.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;

namespace Gravel.Storage.FileSystem.Sst;

public sealed class FileSstWriter : ISstWriter, IAsyncDisposable
{
    const ulong RocksMagic = 0xDB4775248B80FB57UL;
    readonly IBlockCompressor _compressor;
    readonly DataBlockBuilder _data = new();
    readonly string _finalPath;
    readonly FullFilterBlockBuilder _fullFilter;
    readonly SimpleBlockBuilder _index = new();

    // Initialization task started by ctor; currently trivial but allows async init without blocking ctor
    readonly Task _initTask;
    readonly ILogger _logger;
    readonly SimpleBlockBuilder _metaindex = new();

    // Range tombstones buffer (sorted and flushed at end)
    readonly RangeDeleteBlockBuilder _rangeDeletes = new();
    readonly FileStream _stream;
    readonly int _targetBlockSize;
    readonly string _tmpPath;
    int _entryCount;

    byte[] _lastKey = [];

    public FileSstWriter(
        string path,
        int expectedEntries,
        int bufferSize,
        int blockSize,
        IBlockCompressor? compressor,
        ILogger? logger)
    {
        _finalPath = path;
        _tmpPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        _logger = logger ?? NullLogger.Instance;
        _compressor = compressor ?? new DefaultCompressor();
        _targetBlockSize = Math.Max(4096, blockSize);
        _fullFilter = new FullFilterBlockBuilder(expectedEntries);

        _stream = new FileStream(
            _tmpPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        Log.SstOpenedWrite(_logger, _finalPath, _tmpPath, bufferSize, 16);

        // Kick off a trivial init task so callers can await InitializeAsync if desired.
        _initTask = Task.CompletedTask;
    }

    public async ValueTask WriteAsync(IAsyncEnumerable<DbEntry> entries, CancellationToken ct = default)
    {
        await foreach (var e in entries.WithCancellation(ct).ConfigureAwait(false))
        {
            // On-disk type encoding: Put=1, DeleteKey=0, DeleteRange=2
            byte type = e.Kind switch
            {
                DbEntryKind.Put => 0x1,
                DbEntryKind.DeleteKey => 0x0,
                DbEntryKind.DeleteRange => 0x2,
                _ => throw new InvalidOperationException()
            };

            if (e.Kind == DbEntryKind.DeleteRange)
            {
                // Buffer range tombstone in separate structure
                _rangeDeletes.Add(e.Key.Span, e.Value.Span, e.Sequence);
                continue;
            }

            var ikey = MakeInternalKey(e.Key.Span, e.Sequence, type);
            _fullFilter.AddKey(e.Key.Span);
            _data.Add(ikey, e.Kind == DbEntryKind.Put ? e.Value.Span : ReadOnlySpan<byte>.Empty);
            _lastKey = ikey;
            _entryCount++;
            if (_data.CurrentSize >= _targetBlockSize)
                await FlushDataBlockAsync(ct).ConfigureAwait(false);
        }
    }

    public async ValueTask FlushAsync(CancellationToken ct = default)
    {
        await FlushDataBlockAsync(ct).ConfigureAwait(false);

        // Write range-deletion block if any and add to metaindex
        if (_rangeDeletes.Count > 0)
        {
            var rdb = _rangeDeletes.Finish();
            var rdbHandle = await WriteRawBlockAsync(rdb, CompressionKind.None, ct);
            _metaindex.Add("range.delete"u8, rdbHandle);
        }

        var filterHandle = await WriteRawBlockAsync(_fullFilter.Finish(), CompressionKind.None, ct);
        _metaindex.Add("filter.full"u8, filterHandle);
        var indexHandle = await WriteRawBlockAsync(_index.Finish(), CompressionKind.None, ct);
        var metaHandle = await WriteRawBlockAsync(_metaindex.Finish(), CompressionKind.None, ct);

        Span<byte> footer = stackalloc byte[48];
        var n = metaHandle.Encode(footer[..20]);
        for (var i = n; i < 20; i++) footer[i] = 0;
        var n2 = 20;
        n2 += indexHandle.Encode(footer.Slice(n2, 20));
        for (var i = n2; i < 40; i++) footer[i] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(footer.Slice(40, 8), RocksMagic);
        _stream.Write(footer);

        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            if (File.Exists(_finalPath)) File.Delete(_finalPath);
            if (File.Exists(_tmpPath)) File.Move(_tmpPath, _finalPath);
            Log.SstSealed(_logger, _finalPath, _tmpPath);
        }
        catch (Exception ex)
        {
            Log.SstSealFailed(_logger, _finalPath, _tmpPath, ex.Message);
        }
    }

    /// <summary>
    ///     Ensure writer has completed any async initialization. Safe to call multiple times.
    ///     Currently a no-op but provided for API symmetry and future async init needs.
    /// </summary>
    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return ValueTask.FromCanceled(ct);
        return _initTask.IsCompletedSuccessfully ? ValueTask.CompletedTask : new ValueTask(_initTask);
    }

    static byte[] MakeInternalKey(ReadOnlySpan<byte> userKey, ulong seq, byte type)
    {
        var buf = new byte[userKey.Length + 8];
        userKey.CopyTo(buf);
        var trailer = seq << 8 | type;
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(userKey.Length, 8), trailer);
        return buf;
    }

    async ValueTask FlushDataBlockAsync(CancellationToken ct)
    {
        if (_data.CurrentSize == 0) return;
        var raw = _data.Finish();
        var comp = _compressor.Compress(raw);

        // compute crc over payload + compType without using large stackalloc
        uint crc;
        var compType = (byte)_compressor.Kind;
        if (comp.Length <= 1024)
        {
            Span<byte> crcInput = stackalloc byte[comp.Length + 1];
            comp.CopyTo(crcInput);
            crcInput[^1] = compType;
            crc = Crc32C.Compute(crcInput);
        }
        else
        {
            var pooled = ArrayPool<byte>.Shared.Rent(comp.Length + 1);
            try
            {
                comp.CopyTo(pooled.AsSpan(0, comp.Length));
                pooled[comp.Length] = compType;
                crc = Crc32C.Compute(pooled.AsSpan(0, comp.Length + 1));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pooled);
            }
        }

        var offset = _stream.Position;
        await _stream.WriteAsync(comp, ct).ConfigureAwait(false);
        Span<byte> trailer = stackalloc byte[5];
        trailer[0] = compType;
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[1..], crc);
        _stream.Write(trailer);

        var size = _stream.Position - offset;
        _index.Add(_lastKey, new BlockHandle((ulong)offset, (ulong)size));
        _data.Reset();
    }

    async ValueTask<BlockHandle> WriteRawBlockAsync(byte[] raw, CompressionKind comp, CancellationToken ct)
    {
        var payload = comp == CompressionKind.None ? raw : _compressor.Compress(raw);
        var offset = _stream.Position;

        // compute crc using pooled buffer for large payloads
        uint crc;
        var compType = (byte)comp;
        if (payload.Length <= 1024)
        {
            Span<byte> crcInput = stackalloc byte[payload.Length + 1];
            payload.CopyTo(crcInput);
            crcInput[^1] = compType;
            crc = Crc32C.Compute(crcInput);
        }
        else
        {
            var pooled = ArrayPool<byte>.Shared.Rent(payload.Length + 1);
            try
            {
                payload.CopyTo(pooled.AsSpan(0, payload.Length));
                pooled[payload.Length] = compType;
                crc = Crc32C.Compute(pooled.AsSpan(0, payload.Length + 1));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pooled);
            }
        }

        await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
        Span<byte> trailer = stackalloc byte[5];
        trailer[0] = compType;
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[1..], crc);
        _stream.Write(trailer);

        var size = _stream.Position - offset;
        return new BlockHandle((ulong)offset, (ulong)size);
    }
}
