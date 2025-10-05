using System.Buffers;
using System.Buffers.Binary;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression.Default;
using Gravel.Internals;
using Gravel.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Storage.FileSystem.Sst;

public sealed class FileSstWriter : ISstWriter
{
    const ulong RocksMagic = 0xDB4775248B80FB57UL;
    readonly IBlockCompressor _compressor;
    readonly DataBlockBuilder _data = new();
    readonly string _finalPath;
    readonly FullFilterBlockBuilder _fullFilter;
    readonly SimpleBlockBuilder _index = new();

    // Initialization task started by ctor; currently trivial but allows async init without blocking ctor
    readonly Task _initTask;

    readonly byte[] _lastKey = [];
    readonly ILogger _logger;
    readonly SimpleBlockBuilder _metaindex = new();

    // Range tombstones buffer (sorted and flushed at end)
    readonly RangeDeleteBlockBuilder _rangeDeletes = new();
    readonly FileStream _stream;
    readonly int _targetBlockSize;
    readonly string _tmpPath;
    int _entryCount;

    // New pooled last key buffer and length
    byte[]? _lastKeyBuf;
    int _lastKeyLen;

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
        byte[]? tempKeyBuf = null;
        try
        {
            await foreach (var entry in entries.WithCancellation(ct).ConfigureAwait(false))
            {
                if (!HandleEntry(entry, ref tempKeyBuf))
                    continue;

                _entryCount++;
                if (_data.CurrentSize >= _targetBlockSize)
                    await FlushDataBlockAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (tempKeyBuf != null)
                ArrayPool<byte>.Shared.Return(tempKeyBuf);
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
        finally
        {
            if (_lastKeyBuf != null)
            {
                ArrayPool<byte>.Shared.Return(_lastKeyBuf);
                _lastKeyBuf = null;
                _lastKeyLen = 0;
            }
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

    bool HandleEntry(DbEntry entry, ref byte[]? tempKeyBuf)
    {
        if (entry.Kind == DbEntryKind.DeleteRange)
        {
            _rangeDeletes.Add(entry.Key.Span, entry.Value.Span, entry.Sequence);
            return false;
        }

        byte type = entry.Kind switch
        {
            DbEntryKind.Put => 0x1,
            DbEntryKind.DeleteKey => 0x0,
            _ => throw new InvalidOperationException()
        };

        var needed = entry.Key.Length + 8;
        tempKeyBuf = EnsureBuffer(tempKeyBuf, needed);
        entry.Key.Span.CopyTo(tempKeyBuf.AsSpan(0, entry.Key.Length));
        var trailer = entry.Sequence << 8 | type;
        BinaryPrimitives.WriteUInt64LittleEndian(tempKeyBuf.AsSpan(entry.Key.Length, 8), trailer);
        var ikeySpan = tempKeyBuf.AsSpan(0, needed);

        _fullFilter.AddKey(entry.Key.Span);
        _data.Add(ikeySpan, entry.Kind == DbEntryKind.Put ? entry.Value.Span : ReadOnlySpan<byte>.Empty);

        _lastKeyBuf = EnsureBuffer(_lastKeyBuf, needed);
        ikeySpan.CopyTo(_lastKeyBuf.AsSpan(0, needed));
        _lastKeyLen = needed;

        return true;
    }

    async ValueTask FlushDataBlockAsync(CancellationToken ct)
    {
        if (_data.CurrentSize == 0) return;

        var pooled = _data.FinishPooled();
        try
        {
            var raw = pooled.Buffer.AsSpan(0, pooled.Length);
            var (compArr, compLen, compType) = CompressBlock(raw);
            var crc = ComputeBlockCrc(compArr, compLen, compType);

            var offset = _stream.Position;
            await _stream.WriteAsync(compArr.AsMemory(0, compLen), ct).ConfigureAwait(false);
            Span<byte> trailer = stackalloc byte[5];
            trailer[0] = compType;
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[1..], crc);
            _stream.Write(trailer);

            var size = _stream.Position - offset;

            // add to index using pooled last key
            if (_lastKeyBuf != null && _lastKeyLen > 0)
                _index.Add(_lastKeyBuf.AsSpan(0, _lastKeyLen), new BlockHandle((ulong)offset, (ulong)size));
            else
                _index.Add(_lastKey.AsSpan(), new BlockHandle((ulong)offset, (ulong)size));

            _data.Reset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled.Buffer);
        }
    }

    (byte[] compArr, int compLen, byte compType) CompressBlock(ReadOnlySpan<byte> raw)
    {
        var compType = (byte)_compressor.Kind;
        if (_compressor.TryCompress(raw, Span<byte>.Empty, out _))
        {
            var maxLen = _compressor.GetMaxCompressedLength(raw.Length);
            using var scope = Buf.Rent(maxLen);
            var pooledBuf = scope.Buffer!;
            if (_compressor.TryCompress(raw, pooledBuf, out var compLen))
            {
                // If the compressed data fills the buffer exactly, return it directly
                if (compLen == pooledBuf.Length)
                {
                    return (pooledBuf, compLen, compType);
                }

                // Otherwise, slice the buffer to the actual length
                var resultArr = new byte[compLen];
                Array.Copy(pooledBuf, 0, resultArr, 0, compLen);
                return (resultArr, compLen, compType);
            }

            var fallbackArr = _compressor.Compress(raw);
            return (fallbackArr, fallbackArr.Length, compType);
        }

        var arr2 = _compressor.Compress(raw);
        return (arr2, arr2.Length, compType);
    }

    static uint ComputeBlockCrc(byte[] compArr, int compLen, byte compType)
    {
        using var scope = Buf.Rent(compLen + 1);
        var crcInput = scope.Span;
        new ReadOnlySpan<byte>(compArr, 0, compLen).CopyTo(crcInput);
        crcInput[compLen] = compType;
        return Crc32C.Compute(crcInput);
    }

    async ValueTask<BlockHandle> WriteRawBlockAsync(byte[] raw, CompressionKind comp, CancellationToken ct)
    {
        var payload = comp == CompressionKind.None ? raw : _compressor.Compress(raw);
        var offset = _stream.Position;


        var compType = (byte)comp;

        var bufLen = payload.Length + 1;
        using var pooled = Buf.AsyncRent(bufLen);
        payload.CopyTo(pooled.Span);
        pooled.Span[payload.Length] = compType;
        var checksum = Crc32C.Compute(pooled.Span[..bufLen]);
        await _stream.WriteAsync(payload, ct).ConfigureAwait(false);


        Span<byte> trailer = stackalloc byte[5];
        trailer[0] = compType;
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[1..], checksum);
        _stream.Write(trailer);

        var size = _stream.Position - offset;
        return new BlockHandle((ulong)offset, (ulong)size);
    }

    byte[] EnsureBuffer(byte[]? buffer, int size)
    {
        if (buffer == null || buffer.Length < size)
        {
            if (buffer != null)
                ArrayPool<byte>.Shared.Return(buffer);
            buffer = ArrayPool<byte>.Shared.Rent(size);
        }

        return buffer;
    }
}
