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
using Gravel.Storage.Shared;
using Gravel.Telemetry;
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

    /// <summary>
    ///     Initializes a new instance of the <see cref="FileSstReader" /> class.
    /// </summary>
    /// <param name="path">Path to the SST file to open.</param>
    /// <param name="compressorFactory">Factory used to create decompressors for blocks.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="InvalidDataException">Thrown when the file is not a valid Rocks/Pebble SST.</exception>
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

    /// <summary>
    ///     Releases resources held by the reader.
    /// </summary>
    public void Dispose()
    {
        _stream.Dispose();
        _mmf.Dispose();
    }

    /// <summary>
    ///     Ensures the reader has completed asynchronous initialization.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when initialization has finished.</returns>
    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        return _initTask.IsCompletedSuccessfully ? ValueTask.CompletedTask : new ValueTask(_initTask);
    }

    /// <summary>
    ///     Checks whether the given key might be present in the SST using the full filter if available.
    /// </summary>
    /// <param name="key">The user key to test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the key may be present; false if definitely not.</returns>
    public ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        TelemetrySources.SstReads.Add(1, new KeyValuePair<string, object?>("op", "might_contain"));
        if (!_initTask.IsCompleted)
            return new ValueTask<bool>(_initTask.ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception!;
                return _filter == null || _filter.MightContain(key.Span);
            }, TaskScheduler.Default));

        return ValueTask.FromResult(_filter == null || _filter.MightContain(key.Span));
    }

    /// <summary>
    ///     Gets the range delete tombstones contained in the SST.
    /// </summary>
    /// <returns>A read-only list of ranges with their sequence numbers.</returns>
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

    /// <summary>
    ///     Looks up a key in the SST and returns the latest visible entry, honoring range deletes.
    /// </summary>
    /// <param name="key">The user key to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching <see cref="DbEntry" /> or null if not found or masked.</returns>
    public async ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        TelemetrySources.SstReads.Add(1, new KeyValuePair<string, object?>("op", "get"));
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

    /// <summary>
    ///     Reads all entries in key order, yielding only those not masked by range deletes.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of <see cref="DbEntry" /> values.</returns>
    public async IAsyncEnumerable<DbEntry> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        TelemetrySources.SstReads.Add(1, new KeyValuePair<string, object?>("op", "read_all"));
        await InitializeAsync(ct).ConfigureAwait(false);

        foreach (var (_, handle) in _indexEntries)
        {
            var block = await ReadBlockAsync(handle).ConfigureAwait(false);

            // Iterate entries using DbEntryLight to avoid per-entry allocations.
            var restartsCount = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(block.Length - 4));
            var restartsOff = block.Length - 4 - restartsCount * 4;
            var pos = 0;

            using var scope = Buf.AsyncRent(256);
            var keyBuf = scope.Buffer;
            var keyLen = 0;

            while (TryReadNextEntryLight(block, restartsOff, ref pos, keyBuf, ref keyLen, out var light))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsMaskedByRange(light.Key, light.Sequence))
                    yield return light.ToOwned();
            }
        }
    }

    /// <summary>
    ///     Reads a block from the SST file, decompresses and validates its contents.
    /// </summary>
    /// <param name="handle">The block handle specifying offset and size.</param>
    /// <returns>The decompressed block as a byte array.</returns>
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

    /// <summary>
    ///     Validates the CRC32C checksum of a block.
    /// </summary>
    /// <param name="data">The block data.</param>
    /// <param name="compType">The compression type byte.</param>
    /// <param name="expectedCrc">The expected CRC value.</param>
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

    /// <summary>
    ///     Copies an uncompressed block into a new byte array.
    /// </summary>
    /// <param name="data">The block data.</param>
    /// <returns>A new byte array containing the block data.</returns>
    static byte[] CopyUncompressedBlock(ReadOnlySpan<byte> data)
    {
        using var scope = Buf.AsyncRent(data.Length);
        var pooled = scope.Span;
        data.CopyTo(pooled);
        var result = new byte[data.Length];
        pooled[..data.Length].CopyTo(result);
        return result;
    }

    /// <summary>
    ///     Decompresses a block using Snappy compression.
    /// </summary>
    /// <param name="data">The compressed block data.</param>
    /// <returns>The decompressed block as a byte array.</returns>
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

    /// <summary>
    ///     Finds the index entry for a given key using binary search.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>The index of the entry if found; otherwise, -1.</returns>
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

    /// <summary>
    ///     Searches for a matching entry in a data block using lightweight parsing.
    ///     Only materializes a <see cref="DbEntry" /> if a match is found.
    /// </summary>
    /// <param name="block">The SST data block buffer.</param>
    /// <param name="key">The key to search for.</param>
    /// <returns>The matching <see cref="DbEntry" /> if found; otherwise, null.</returns>
    DbEntry? FindEntryInBlock(byte[] block, ReadOnlySpan<byte> key)
    {
        // Iterate using light entries and materialize only on match
        var restartsCount = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(block.Length - 4));
        var restartsOff = block.Length - 4 - restartsCount * 4;
        var pos = 0;

        using var scope = Buf.AsyncRent(256);
        var keyBuf = scope.Buffer;
        var keyLen = 0;

        while (TryReadNextEntryLight(block, restartsOff, ref pos, keyBuf, ref keyLen, out var light))
        {
            var cmp = ByteComparer.Compare(light.Key, key);
            if (cmp == 0)
                return light.ToOwned();
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

    /// <summary>
    ///     Decodes a <see cref="BlockHandle" /> from a span containing varint-encoded offset and size.
    /// </summary>
    /// <param name="span">The span containing the encoded block handle.</param>
    /// <returns>The decoded <see cref="BlockHandle" />.</returns>
    static BlockHandle DecodeBlockHandle(ReadOnlySpan<byte> span)
    {
        var off = VarInt.Read64(ref span);
        var size = VarInt.Read64(ref span);
        return new BlockHandle(off, size);
    }

    /// <summary>
    ///     Parses a key-value block from SST metadata or index.
    /// </summary>
    /// <param name="raw">The raw block buffer.</param>
    /// <returns>A list of key-value pairs as byte arrays.</returns>
    static List<(byte[] key, byte[] value)> ParseKeyValueBlock(byte[] raw)
    {
        var list = new List<(byte[], byte[])>();
        var pos = 0;
        while (pos < raw.Length)
        {
            var klen = (int)VarInt.Read32(raw, ref pos);
            var key = raw.AsSpan(pos, klen).ToArray();
            pos += klen;

            var vlen = (int)VarInt.Read32(raw, ref pos);
            var val = raw.AsSpan(pos, vlen).ToArray();
            pos += vlen;

            list.Add((key, val));
        }

        return list;
    }

    /// <summary>
    ///     Parses a range delete block from SST metadata.
    /// </summary>
    /// <param name="raw">The raw block buffer.</param>
    /// <returns>A sequence of range tombstones (start, end, sequence).</returns>
    static IEnumerable<(byte[] Start, byte[] End, ulong Seq)> ParseRangeDeleteBlock(byte[] raw)
    {
        var list = new List<(byte[], byte[], ulong)>();
        var pos = 0;
        while (pos < raw.Length)
        {
            var slen = (int)VarInt.Read32(raw, ref pos);
            var start = raw.AsSpan(pos, slen).ToArray();
            pos += slen;

            var elen = (int)VarInt.Read32(raw, ref pos);
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
    // Data block parser (lightweight with DbEntryLight, materialize on demand)
    // ---------------------------------------------------------------------

    /// <summary>
    ///     Attempts to parse the next entry in an SST data block as a lightweight <see cref="DbEntryLight" />.
    ///     This avoids heap allocations by using stack-allocated buffers and spans.
    /// </summary>
    /// <param name="raw">The raw SST data block buffer.</param>
    /// <param name="restartsOff">Offset to the restart array (end of entries).</param>
    /// <param name="pos">Current position in the buffer (updated on success).</param>
    /// <param name="keyBuf">Stack-allocated buffer for key reconstruction.</param>
    /// <param name="keyLen">Current key length (updated on success).</param>
    /// <param name="entry">The parsed <see cref="DbEntryLight" /> if successful.</param>
    /// <returns>True if an entry was parsed (maybe malformed); false if end of block.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryReadNextEntryLight(
        byte[] raw, int restartsOff, ref int pos, byte[] keyBuf, ref int keyLen, out DbEntryLight entry)
    {
        // default init
        entry = default;
        if (pos >= restartsOff)
            return false;

        var shared = (int)VarInt.Read32(raw, ref pos);
        var unshared = (int)VarInt.Read32(raw, ref pos);
        var vlen = (int)VarInt.Read32(raw, ref pos);

        var needed = shared + unshared;
        if (needed > keyBuf.Length)
            throw new InvalidOperationException("Key buffer too small; increase Buf.AsyncRent size if needed.");

        raw.AsSpan(pos, unshared).CopyTo(keyBuf.AsSpan(shared, unshared));
        keyLen = shared + unshared;
        pos += unshared;

        var valueSpan = raw.AsSpan(pos, vlen);
        pos += vlen;

        if (keyLen < 8)
            return true; // skip malformed entry silently

        var trailer = BinaryPrimitives.ReadUInt64LittleEndian(keyBuf.AsSpan(keyLen - 8, 8));
        var seq = trailer >> 8;
        var rawType = (byte)(trailer & 0xFF);
        var kind = rawType switch
        {
            1 => DbEntryKind.Put,
            0 => DbEntryKind.DeleteKey,
            2 => DbEntryKind.DeleteRange,
            _ => throw new InvalidDataException("Unknown entry type in data block")
        };
        var userLen = keyLen - 8;

        var keySpan = keyBuf.AsSpan(0, userLen);
        var valSpan = kind == DbEntryKind.Put ? valueSpan :
            kind == DbEntryKind.DeleteRange ? valueSpan : ReadOnlySpan<byte>.Empty;

        entry = new DbEntryLight(keySpan, valSpan, seq, kind);
        return true;
    }

    // ---------------------------------------------------------------------
    // Range masking
    // ---------------------------------------------------------------------

    /// <summary>
    ///     Determines if a key is masked by any range tombstone with a higher sequence number.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="seq">The sequence number to compare against.</param>
    /// <returns>True if the key is masked by a range tombstone; otherwise, false.</returns>
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
