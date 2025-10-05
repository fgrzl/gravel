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
    readonly ILogger _logger;
    readonly SimpleBlockBuilder _metaindex = new();

    // Range tombstones buffer (sorted and flushed at end)
    readonly RangeDeleteBlockBuilder _rangeDeletes = new();
    readonly FileStream _stream;
    readonly int _targetBlockSize;
    readonly string _tmpPath;
    int _entryCount;

    readonly byte[] _lastKey = [];

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
        // Rent a reusable buffer for composing internal keys (user key + 8-byte trailer)
        byte[]? tempKeyBuf = null;
        try
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

                var needed = e.Key.Length + 8;
                if (tempKeyBuf == null)
                {
                    tempKeyBuf = ArrayPool<byte>.Shared.Rent(Math.Max(needed, 256));
                }
                else if (needed > tempKeyBuf.Length)
                {
                    ArrayPool<byte>.Shared.Return(tempKeyBuf);
                    tempKeyBuf = ArrayPool<byte>.Shared.Rent(Math.Max(needed, tempKeyBuf.Length * 2));
                }

                // compose internal key into tempKeyBuf
                e.Key.Span.CopyTo(tempKeyBuf.AsSpan(0, e.Key.Length));
                var trailer = e.Sequence << 8 | type;
                BinaryPrimitives.WriteUInt64LittleEndian(tempKeyBuf.AsSpan(e.Key.Length, 8), trailer);

                var ikeySpan = tempKeyBuf.AsSpan(0, needed);

                _fullFilter.AddKey(e.Key.Span);
                _data.Add(ikeySpan, e.Kind == DbEntryKind.Put ? e.Value.Span : ReadOnlySpan<byte>.Empty);

                // store last key into pooled buffer to avoid allocating per entry
                if (_lastKeyBuf == null)
                {
                    _lastKeyBuf = ArrayPool<byte>.Shared.Rent(Math.Max(needed, 256));
                }
                else if (needed > _lastKeyBuf.Length)
                {
                    ArrayPool<byte>.Shared.Return(_lastKeyBuf);
                    _lastKeyBuf = ArrayPool<byte>.Shared.Rent(Math.Max(needed, _lastKeyBuf.Length * 2));
                }

                ikeySpan.CopyTo(_lastKeyBuf.AsSpan(0, needed));
                _lastKeyLen = needed;

                _entryCount++;
                if (_data.CurrentSize >= _targetBlockSize)
                    await FlushDataBlockAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (tempKeyBuf != null)
            {
                ArrayPool<byte>.Shared.Return(tempKeyBuf);
            }
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

    async ValueTask FlushDataBlockAsync(CancellationToken ct)
    {
        if (_data.CurrentSize == 0) return;

        // Use pooled finish to get buffer and length without allocating a new array
        var pooled = _data.FinishPooled();
        try
        {
            var raw = pooled.Buffer.AsSpan(0, pooled.Length);

            byte[]? compArr = null;
            int compLen;
            var rentedComp = false;

            if (_compressor.TryCompress(raw, Span<byte>.Empty, out _))
            {
                // If compressor supports TryCompress, use a local pooled buffer sized by GetMaxCompressedLength
                var maxLen = _compressor.GetMaxCompressedLength(raw.Length);
                compArr = ArrayPool<byte>.Shared.Rent(maxLen);
                rentedComp = true;
                if (!_compressor.TryCompress(raw, compArr, out compLen))
                {
                    // fallback to legacy path
                    ArrayPool<byte>.Shared.Return(compArr);
                    rentedComp = false;
                    compArr = _compressor.Compress(raw);
                    compLen = compArr.Length;
                }
            }
            else
            {
                // fall back to allocation-returning API
                var tmp = _compressor.Compress(raw);
                compArr = tmp;
                compLen = tmp.Length;
            }

            // compute crc
            uint crc;
            var compType = (byte)_compressor.Kind;
            if (compLen <= 1024)
            {
                Span<byte> crcInput = stackalloc byte[compLen + 1];
                new ReadOnlySpan<byte>(compArr, 0, compLen).CopyTo(crcInput);
                crcInput[^1] = compType;
                crc = Crc32C.Compute(crcInput);
            }
            else
            {
                var pooledCrc = ArrayPool<byte>.Shared.Rent(compLen + 1);
                try
                {
                    new ReadOnlySpan<byte>(compArr, 0, compLen).CopyTo(pooledCrc.AsSpan(0, compLen));
                    pooledCrc[compLen] = compType;
                    crc = Crc32C.Compute(pooledCrc.AsSpan(0, compLen + 1));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(pooledCrc);
                }
            }

            var offset = _stream.Position;
            await _stream.WriteAsync(compArr.AsMemory(0, compLen), ct).ConfigureAwait(false);
            Span<byte> trailer = stackalloc byte[5];
            trailer[0] = compType;
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[1..], crc);
            _stream.Write(trailer);

            var size = _stream.Position - offset;

            // add to index using pooled last key
            if (_lastKeyBuf != null && _lastKeyLen > 0)
            {
                _index.Add(_lastKeyBuf.AsSpan(0, _lastKeyLen), new BlockHandle((ulong)offset, (ulong)size));
            }
            else
            {
                _index.Add(_lastKey.AsSpan(), new BlockHandle((ulong)offset, (ulong)size));
            }

            _data.Reset();

            // return compressor buffer if it was pooled
            if (rentedComp && compArr != null)
            {
                ArrayPool<byte>.Shared.Return(compArr);
            }
        }
        finally
        {
            // Return pooled data block buffer
            ArrayPool<byte>.Shared.Return(pooled.Buffer);
        }
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
}
