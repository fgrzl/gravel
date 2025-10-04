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

public sealed class FileSstReader : ISstReader
{
    const ulong RocksMagic = 0xDB4775248B80FB57UL;
    readonly ICompressorFactory _compressorFactory;
    readonly List<(byte[] key, BlockHandle handle)> _indexEntries = [];
    readonly BlockHandle _indexHandle;

    // initialization task started by ctor and awaited by public APIs. Never call .Result; await instead.
    readonly Task _initTask;
    readonly ILogger<FileSstReader> _logger;

    // handles discovered from footer, loaded during initialization
    readonly BlockHandle _metaHandle;
    readonly Dictionary<string, BlockHandle> _metaHandles = new();

    // internal mmapped stream
    readonly MemoryMappedFile _mmf;
    readonly string _path;
    readonly List<(byte[] Start, byte[] End, ulong Seq)> _rangeDeletes = [];
    readonly MemoryMappedViewStream _stream;

    // lazily populated during async init
    FullFilter? _filter;

    public FileSstReader(string path, ICompressorFactory compressorFactory, ILogger<FileSstReader>? logger = null)
    {
        _path = path;
        _logger = logger ?? NullLogger<FileSstReader>.Instance;
        _compressorFactory = compressorFactory ?? throw new ArgumentNullException(nameof(compressorFactory));

        // create memory-mapped view for the SST file (read-only)
        var fi = new FileInfo(path);
        var length = fi.Length;
        if (length == 0) throw new InvalidDataException("File is empty");
        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, length, MemoryMappedFileAccess.Read);
        _stream = _mmf.CreateViewStream(0, length, MemoryMappedFileAccess.Read);

        // read footer (48 bytes at end of file) synchronously — small fixed read
        Span<byte> footer = stackalloc byte[48];
        if (length < 48) throw new InvalidDataException("File too small to be a Rocks/Pebble SST");
        var footerPos = length - 48;
        _stream.Seek(footerPos, SeekOrigin.Begin);
        _stream.ReadExactly(footer);

        var magic = BinaryPrimitives.ReadUInt64LittleEndian(footer.Slice(40, 8));
        if (magic != RocksMagic)
            throw new InvalidDataException("Not a Rocks/Pebble SST");

        _metaHandle = DecodeBlockHandle(footer[..20]);
        _indexHandle = DecodeBlockHandle(footer.Slice(20, 20));

        // Kick off async initialization but do not block here. Errors will surface when awaiting InitializeAsync
        _initTask = InitializeInternalAsync();

        Log.SstOpenedRead(_logger, path, 64 * 1024);
    }

    public IReadOnlyList<(ReadOnlyMemory<byte> Start, ReadOnlyMemory<byte> End, ulong Seq)> GetRangeDeletes()
    {
        // Return as ReadOnlyMemory without copying
        return _rangeDeletes
            .Select(r => ((ReadOnlyMemory<byte>)r.Start, (ReadOnlyMemory<byte>)r.End, r.Seq))
            .ToList().AsReadOnly();
    }

    // ----- Public API -----

    public async ValueTask<DbEntry?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        // ensure initialization completed
        await InitializeAsync(ct).ConfigureAwait(false);

        if (_filter != null && !_filter.MightContain(key.Span))
            return null;

        // find index entry >= key using binary search
        int lo = 0, hi = _indexEntries.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = lo + hi >>> 1;
            var cmp = ByteComparer.Compare(key.Span, _indexEntries[mid].key);
            if (cmp <= 0)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        if (found >= 0)
        {
            var handle = _indexEntries[found].handle;
            var block = await ReadBlock(handle).ConfigureAwait(false);
            foreach (var e in ParseDataBlock(block))
            {
                var cmp = ByteComparer.Compare(e.Key.Span, key.Span);
                if (cmp == 0)
                {
                    // Apply range tombstone masking within this file
                    if (IsMaskedByRange(key.Span, e.Sequence)) return null;
                    return e;
                }

                if (cmp > 0) break;
            }
        }

        // No exact entry; still could be masked by a range tombstone in this SST
        if (IsMaskedByRange(key.Span, ulong.MaxValue)) return DbEntry.DeleteKey(key, ulong.MaxValue);
        return null;
    }

    public async IAsyncEnumerable<DbEntry> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);

        foreach (var (_, handle) in _indexEntries)
        {
            var block = await ReadBlock(handle).ConfigureAwait(false);
            foreach (var e in ParseDataBlock(block))
            {
                ct.ThrowIfCancellationRequested();
                // Apply range tombstones at reader level so higher layers can be simpler
                if (!IsMaskedByRange(e.Key.Span, e.Sequence))
                    yield return e;
            }
        }
    }

    public ValueTask<bool> MightContainAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        // eagerly await init if not ready
        if (!_initTask.IsCompleted)
            return new ValueTask<bool>(_initTask.ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception!;
                return _filter == null || _filter.MightContain(key.Span);
            }, TaskScheduler.Default));

        if (_filter == null) return ValueTask.FromResult(true);
        return ValueTask.FromResult(_filter.MightContain(key.Span));
    }

    public void Dispose()
    {
        _stream.Dispose();
        _mmf.Dispose();
    }

    /// <summary>
    ///     Ensure the reader has finished loading meta/index/filter/range-deletes.
    ///     Safe to call multiple times.
    /// </summary>
    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return ValueTask.FromCanceled(ct);
        // Return a ValueTask wrapping the internal init task
        return _initTask.IsCompletedSuccessfully ? ValueTask.CompletedTask : new ValueTask(_initTask);
    }

    // Internal initialization that loads metaindex, filter, ranges and index entries.
    async Task InitializeInternalAsync()
    {
        // load metaindex
        var metaRaw = await ReadBlock(_metaHandle).ConfigureAwait(false);
        var metaEntries = ParseKeyValueBlock(metaRaw);
        foreach (var (k, v) in metaEntries)
        {
            var bh = DecodeBlockHandle(v);
            _metaHandles[Encoding.ASCII.GetString(k)] = bh;
        }

        // load filter if present
        if (_metaHandles.TryGetValue("filter.full", out var fh))
        {
            var fb = await ReadBlock(fh).ConfigureAwait(false);
            _filter = new FullFilter(fb);
        }

        // load range deletes if present
        if (_metaHandles.TryGetValue("range.delete", out var rdh))
        {
            var rdb = await ReadBlock(rdh).ConfigureAwait(false);
            _rangeDeletes.AddRange(ParseRangeDeleteBlock(rdb));
        }

        // load index
        var indexRaw = await ReadBlock(_indexHandle).ConfigureAwait(false);
        var indexEntries = ParseKeyValueBlock(indexRaw);
        foreach (var (k, v) in indexEntries)
        {
            var bh = DecodeBlockHandle(v);
            _indexEntries.Add((k, bh));
        }
    }

    static BlockHandle DecodeBlockHandle(ReadOnlySpan<byte> span)
    {
        var off = Varint.Read64(ref span);
        var size = Varint.Read64(ref span);
        return new BlockHandle(off, size);
    }

    async Task<byte[]> ReadBlock(BlockHandle handle)
    {
        _stream.Seek((long)handle.Offset, SeekOrigin.Begin);
        var len = (int)handle.Size;
        var buf = new byte[len];
        await _stream.ReadExactlyAsync(buf).ConfigureAwait(false);

        // split into data, trailer
        var trailerLen = 5;
        var dataLen = len - trailerLen;
        var compType = buf[dataLen];
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(dataLen + 1, 4));

        // crc over data + compType
        Span<byte> crcInput = stackalloc byte[dataLen + 1];
        buf.AsSpan(0, dataLen).CopyTo(crcInput);
        crcInput[^1] = compType;
        var calc = Crc32C.Compute(crcInput);
        if (calc != storedCrc) throw new InvalidDataException("CRC mismatch in block");

        var payload = new byte[dataLen];
        Array.Copy(buf, 0, payload, 0, dataLen);

        return compType switch
        {
            (byte)CompressionKind.None => payload,
            (byte)CompressionKind.Snappy => _compressorFactory.Get(CompressionKind.Snappy).Decompress(payload),
            _ => throw new NotSupportedException($"Compression {compType} not supported")
        };
    }

    // Rocks/Pebble index & metaindex blocks = key/value with varint lengths
    static List<(byte[] key, byte[] value)> ParseKeyValueBlock(byte[] raw)
    {
        var list = new List<(byte[], byte[])>();
        var pos = 0;
        while (pos < raw.Length)
        {
            var klen = Varint.Read32(raw, ref pos);
            if (pos + klen > raw.Length) break;
            var key = new byte[klen];
            Array.Copy(raw, pos, key, 0, (int)klen);
            pos += (int)klen;

            var vlen = Varint.Read32(raw, ref pos);
            var val = new byte[vlen];
            Array.Copy(raw, pos, val, 0, (int)vlen);
            pos += (int)vlen;

            list.Add((key, val));
        }

        return list;
    }

    static IEnumerable<(byte[] Start, byte[] End, ulong Seq)> ParseRangeDeleteBlock(byte[] raw)
    {
        var list = new List<(byte[] Start, byte[] End, ulong Seq)>();
        var pos = 0;
        while (pos < raw.Length)
        {
            var slen = Varint.Read32(raw, ref pos);
            var start = new byte[slen];
            Array.Copy(raw, pos, start, 0, (int)slen);
            pos += (int)slen;

            var elen = Varint.Read32(raw, ref pos);
            var end = new byte[elen];
            Array.Copy(raw, pos, end, 0, (int)elen);
            pos += (int)elen;

            var seq = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(pos, 8));
            pos += 8;

            list.Add((start, end, seq));
        }

        // Ensure sorted by start for binary search
        list.Sort((a, b) => ByteComparer.Compare(a.Start, b.Start));
        return list;
    }

    static IEnumerable<DbEntry> ParseDataBlock(byte[] raw)
    {
        var restartsCount = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(raw.Length - 4));
        var restartsOff = raw.Length - 4 - restartsCount * 4;
        var restarts = new int[restartsCount];
        for (var i = 0; i < restartsCount; i++)
            restarts[i] = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(restartsOff + i * 4, 4));

        var pos = 0;
        var prevKey = Array.Empty<byte>();
        while (pos < restartsOff)
        {
            var shared = Varint.Read32(raw, ref pos);
            var unshared = Varint.Read32(raw, ref pos);
            var vlen = Varint.Read32(raw, ref pos);

            var key = new byte[shared + unshared];
            Array.Copy(prevKey, 0, key, 0, (int)shared);
            Array.Copy(raw, pos, key, (int)shared, (int)unshared);
            pos += (int)unshared;

            var val = new byte[vlen];
            Array.Copy(raw, pos, val, 0, (int)vlen);
            pos += (int)vlen;

            prevKey = key;

            // decode internal key trailer
            if (key.Length < 8) continue;
            var trailer = BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(key.Length - 8));
            var seq = trailer >> 8;
            var type = (byte)(trailer & 0xFF);
            var userKey = key.AsMemory(0, key.Length - 8);

            DbEntry? e = type switch
            {
                1 => DbEntry.Put(userKey.ToArray(), val, seq),
                0 => DbEntry.DeleteKey(userKey.ToArray(), seq),
                2 => DbEntry.DeleteRange(userKey.ToArray(), val, seq),
                _ => null
            };
            if (e != null) yield return e.Value;
        }
    }

    bool IsMaskedByRange(ReadOnlySpan<byte> key, ulong seq)
    {
        if (_rangeDeletes.Count == 0) return false;

        // Binary search approximate position by start
        int lo = 0, hi = _rangeDeletes.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + hi >>> 1;
            var cmp = ByteComparer.Compare(_rangeDeletes[mid].Start, key);
            if (cmp <= 0) lo = mid + 1;
            else hi = mid - 1;
        }

        // Walk a small window around position to check coverage; ranges may overlap
        for (var i = Math.Max(0, hi - 4); i <= Math.Min(_rangeDeletes.Count - 1, lo + 4); i++)
        {
            var r = _rangeDeletes[i];
            if (ByteComparer.Compare(r.Start, key) <= 0 && ByteComparer.Compare(key, r.End) < 0)
                if (r.Seq > seq)
                    return true; // newer range masks this seq
        }

        return false;
    }
}
