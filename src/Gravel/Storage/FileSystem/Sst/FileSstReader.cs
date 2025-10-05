using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Sst;
using Gravel.Compression;
using Gravel.Internals;
using Gravel.Internals.Filters;
using Gravel.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Storage.FileSystem.Sst;

/// <summary>
///     High-performance SST file reader using memory-mapped I/O, pooled buffers, and span-based parsing.
/// </summary>
public sealed class FileSstReader : ISstReader
{
    const ulong RocksMagic = 0xDB4775248B80FB57UL;

    readonly ICompressorFactory _compressorFactory;
    readonly List<(byte[] key, BlockHandle handle)> _indexEntries = [];
    readonly BlockHandle _indexHandle;

    readonly Task _initTask;
    readonly ILogger<FileSstReader> _logger;

    readonly BlockHandle _metaHandle;
    readonly Dictionary<string, BlockHandle> _metaHandles = new(StringComparer.Ordinal);
    readonly MemoryMappedFile _mmf;
    readonly string _path;
    readonly List<(byte[] Start, byte[] End, ulong Seq)> _rangeDeletes = [];
    readonly MemoryMappedViewStream _stream;
    FullFilter? _filter;

    public FileSstReader(string path, ICompressorFactory compressorFactory, ILogger<FileSstReader>? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger<FileSstReader>.Instance;
        _compressorFactory = compressorFactory ?? throw new ArgumentNullException(nameof(compressorFactory));

        var fi = new FileInfo(path);
        var length = fi.Length;
        if (length < 48) throw new InvalidDataException("File too small to be a Rocks/Pebble SST");

        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, length, MemoryMappedFileAccess.Read);
        _stream = _mmf.CreateViewStream(0, length, MemoryMappedFileAccess.Read);

        // read footer
        Span<byte> footer = stackalloc byte[48];
        _stream.Seek(length - 48, SeekOrigin.Begin);
        _stream.ReadExactly(footer);

        var magic = BinaryPrimitives.ReadUInt64LittleEndian(footer.Slice(40, 8));
        if (magic != RocksMagic)
            throw new InvalidDataException("Not a Rocks/Pebble SST");

        _metaHandle = DecodeBlockHandle(footer[..20]);
        _indexHandle = DecodeBlockHandle(footer.Slice(20, 20));

        _initTask = InitializeInternalAsync();
        Log.SstOpenedRead(_logger, path, 64 * 1024);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _mmf.Dispose();
    }

    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        return _initTask.IsCompletedSuccessfully ? ValueTask.CompletedTask : new ValueTask(_initTask);
    }

    public ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        if (!_initTask.IsCompleted)
            return new ValueTask<bool>(_initTask.ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception!;
                return _filter == null || _filter.MightContain(key.Span);
            }, TaskScheduler.Default));

        return ValueTask.FromResult(_filter == null || _filter.MightContain(key.Span));
    }

    public IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes()
    {
        return _rangeDeletes
            .Select(r => ((ReadOnlyMemory<byte>)r.Start, (ReadOnlyMemory<byte>)r.End, r.Seq))
            .ToList()
            .AsReadOnly();
    }

    // ---------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------

    public async ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        if (_filter != null && !_filter.MightContain(key.Span))
            return null;

        var found = FindIndexEntry(key.Span);
        if (found >= 0)
        {
            var handle = _indexEntries[found].handle;
            var block = await ReadBlockAsync(handle).ConfigureAwait(false);
            var entry = FindEntryInBlock(block, key.Span);
            if (entry != null)
            {
                if (IsMaskedByRange(key.Span, entry.Value.Sequence)) return null;
                return entry;
            }
        }

        if (IsMaskedByRange(key.Span, ulong.MaxValue))
            return DbEntry.DeleteKey(key.ToArray(), ulong.MaxValue);

        return null;
    }

    public async IAsyncEnumerable<DbEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        foreach (var (_, handle) in _indexEntries)
        {
            var block = await ReadBlockAsync(handle).ConfigureAwait(false);
            foreach (var e in ParseDataBlockOwned(block))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsMaskedByRange(e.Key.Span, e.Sequence))
                    yield return e;
            }
        }
    }

    int FindIndexEntry(ReadOnlySpan<byte> key)
    {
        int lo = 0, hi = _indexEntries.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = lo + hi >> 1;
            var cmp = ByteComparer.Compare(key, _indexEntries[mid].key);
            if (cmp <= 0)
            {
                found = mid;
                hi = mid - 1;
            }
            else lo = mid + 1;
        }

        return found;
    }

    DbEntry? FindEntryInBlock(byte[] block, ReadOnlySpan<byte> key)
    {
        foreach (var e in ParseDataBlockOwned(block))
        {
            var cmp = ByteComparer.Compare(e.Key.Span, key);
            if (cmp == 0)
                return e;
            if (cmp > 0)
                break;
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // Initialization
    // ---------------------------------------------------------------------

    async Task InitializeInternalAsync()
    {
        var metaRaw = await ReadBlockAsync(_metaHandle).ConfigureAwait(false);
        foreach (var (k, v) in ParseKeyValueBlock(metaRaw))
            _metaHandles[Encoding.ASCII.GetString(k)] = DecodeBlockHandle(v);

        if (_metaHandles.TryGetValue("filter.full", out var fh))
        {
            var fb = await ReadBlockAsync(fh).ConfigureAwait(false);
            _filter = new FullFilter(fb);
        }

        if (_metaHandles.TryGetValue("range.delete", out var rdh))
        {
            var rdb = await ReadBlockAsync(rdh).ConfigureAwait(false);
            _rangeDeletes.AddRange(ParseRangeDeleteBlock(rdb));
        }

        var indexRaw = await ReadBlockAsync(_indexHandle).ConfigureAwait(false);
        foreach (var (k, v) in ParseKeyValueBlock(indexRaw))
            _indexEntries.Add((k, DecodeBlockHandle(v)));
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static BlockHandle DecodeBlockHandle(ReadOnlySpan<byte> span)
    {
        var off = Varint.Read64(ref span);
        var size = Varint.Read64(ref span);
        return new BlockHandle(off, size);
    }

    async ValueTask<byte[]> ReadBlockAsync(BlockHandle handle)
    {
        _stream.Seek((long)handle.Offset, SeekOrigin.Begin);
        var len = (int)handle.Size;

        using var scope = Buf.AsyncRent(len);
        var buf = scope.Memory;
        await _stream.ReadExactlyAsync(buf).ConfigureAwait(false);

        const int trailerLen = 5;
        var dataLen = len - trailerLen;
        var compType = buf.Span[dataLen];
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(buf.Span.Slice(dataLen + 1, 4));

        ValidateBlockCrc(buf.Span[..dataLen], compType, storedCrc);

        return compType switch
        {
            (byte)CompressionKind.None => CopyUncompressedBlock(buf.Span[..dataLen]),
            (byte)CompressionKind.Snappy => DecompressSnappyBlock(buf.Span[..dataLen]),
            _ => throw new NotSupportedException($"Compression {compType} not supported")
        };
    }

    static void ValidateBlockCrc(ReadOnlySpan<byte> data, byte compType, uint expectedCrc)
    {
        using var scope = Buf.AsyncRent(data.Length + 1);
        var crcInput = scope.Span;
        data.CopyTo(crcInput);
        crcInput[^1] = compType;
        var actualCrc = Crc32C.Compute(crcInput);
        if (actualCrc != expectedCrc)
            throw new InvalidDataException("CRC mismatch in block");
    }

    static byte[] CopyUncompressedBlock(ReadOnlySpan<byte> data)
    {
        using var scope = Buf.AsyncRent(data.Length);
        var pooled = scope.Span;
        data.CopyTo(pooled);
        var result = new byte[data.Length];
        pooled[..data.Length].CopyTo(result);
        return result;
    }

    byte[] DecompressSnappyBlock(ReadOnlySpan<byte> data)
    {
        var compressor = _compressorFactory.Get(CompressionKind.Snappy);
        if (!compressor.TryGetDecompressedLength(data, out var rawLen))
            return compressor.Decompress(data);

        using var scope = Buf.AsyncRent(rawLen);
        var pooled = scope.Span;
        if (!compressor.TryDecompress(data, pooled, out var written) || written != rawLen)
            return compressor.Decompress(data);

        var result = new byte[rawLen];
        pooled[..rawLen].CopyTo(result);
        return result;
        // fallback to legacy API
    }

    static List<(byte[] key, byte[] value)> ParseKeyValueBlock(byte[] raw)
    {
        var list = new List<(byte[], byte[])>();
        var pos = 0;
        while (pos < raw.Length)
        {
            var klen = (int)Varint.Read32(raw, ref pos);
            var key = raw.AsSpan(pos, klen).ToArray();
            pos += klen;

            var vlen = (int)Varint.Read32(raw, ref pos);
            var val = raw.AsSpan(pos, vlen).ToArray();
            pos += vlen;

            list.Add((key, val));
        }

        return list;
    }

    static IEnumerable<(byte[] Start, byte[] End, ulong Seq)> ParseRangeDeleteBlock(byte[] raw)
    {
        var list = new List<(byte[], byte[], ulong)>();
        var pos = 0;
        while (pos < raw.Length)
        {
            var slen = (int)Varint.Read32(raw, ref pos);
            var start = raw.AsSpan(pos, slen).ToArray();
            pos += slen;

            var elen = (int)Varint.Read32(raw, ref pos);
            var end = raw.AsSpan(pos, elen).ToArray();
            pos += elen;

            var seq = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(pos, 8));
            pos += 8;

            list.Add((start, end, seq));
        }

        list.Sort((a, b) => ByteComparer.Compare(a.Item1, b.Item1));
        return list;
    }

    // ---------------------------------------------------------------------
    // Data block parser (materializes owned DbEntry instances)
    // ---------------------------------------------------------------------

    static IEnumerable<DbEntry> ParseDataBlockOwned(byte[] raw)
    {
        var restartsCount = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(raw.Length - 4));
        var restartsOff = raw.Length - 4 - restartsCount * 4;
        var pos = 0;

        using var scope = Buf.AsyncRent(256);
        var keyBuf = scope.Buffer;
        var keyLen = 0;

        while (pos < restartsOff)
        {
            ParseEntry(raw, ref pos, keyBuf, ref keyLen, out var entry);
            if (entry != null)
                yield return entry.Value;
        }
    }

    static void ParseEntry(byte[] raw, ref int pos, byte[] keyBuf, ref int keyLen, out DbEntry? entry)
    {
        entry = null;
        var shared = (int)Varint.Read32(raw, ref pos);
        var unshared = (int)Varint.Read32(raw, ref pos);
        var vlen = (int)Varint.Read32(raw, ref pos);

        var needed = shared + unshared;
        if (needed > keyBuf.Length)
            throw new InvalidOperationException("Key buffer too small; increase Buf.AsyncRent size if needed.");

        raw.AsSpan(pos, unshared).CopyTo(keyBuf.AsSpan(shared, unshared));
        keyLen = shared + unshared;
        pos += unshared;

        var valueSpan = raw.AsSpan(pos, vlen);
        pos += vlen;

        if (keyLen < 8) return;
        var trailer = BinaryPrimitives.ReadUInt64LittleEndian(keyBuf.AsSpan(keyLen - 8, 8));
        var seq = trailer >> 8;
        var rawType = (byte)(trailer & 0xFF);
        var type = rawType switch
        {
            1 => DbEntryKind.Put,
            0 => DbEntryKind.DeleteKey,
            2 => DbEntryKind.DeleteRange,
            _ => throw new InvalidDataException("Unknown entry type in data block")
        };
        var userLen = keyLen - 8;

        var keyArr = new byte[userLen];
        keyBuf.AsSpan(0, userLen).CopyTo(keyArr);
        var valArr = valueSpan.ToArray();

        entry = type switch
        {
            DbEntryKind.Put => DbEntry.Put(keyArr, valArr, seq),
            DbEntryKind.DeleteKey => DbEntry.DeleteKey(keyArr, seq),
            DbEntryKind.DeleteRange => DbEntry.DeleteRange(keyArr, valArr, seq),
            _ => null
        };
    }

    // ---------------------------------------------------------------------
    // Range masking
    // ---------------------------------------------------------------------

    bool IsMaskedByRange(ReadOnlySpan<byte> key, ulong seq)
    {
        if (_rangeDeletes.Count == 0) return false;

        int lo = 0, hi = _rangeDeletes.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + hi >> 1;
            var cmp = ByteComparer.Compare(_rangeDeletes[mid].Start, key);
            if (cmp <= 0) lo = mid + 1;
            else hi = mid - 1;
        }

        for (var i = Math.Max(0, hi - 4); i <= Math.Min(_rangeDeletes.Count - 1, lo + 4); i++)
        {
            var r = _rangeDeletes[i];
            if (ByteComparer.Compare(r.Start, key) <= 0 &&
                ByteComparer.Compare(key, r.End) < 0 &&
                r.Seq > seq)
                return true;
        }

        return false;
    }
}
