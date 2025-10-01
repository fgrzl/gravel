using System.Buffers.Binary;
using System.Diagnostics;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Internals;
using Gravel.Logging;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Storage.FileSystem.Wal;

public sealed class FileWalWriter : IWalWriter
{
    readonly string _dir;
    readonly ILogger _logger;
    readonly long _segmentSizeLimit;
    readonly object _writeLock = new();
    ulong _currentSegmentId;
    FileStream _stream;

    public FileWalWriter(string directory, long segmentSizeLimit = 64 * 1024 * 1024, ILogger? logger = null)
    {
        _dir = directory;
        _segmentSizeLimit = segmentSizeLimit;
        _currentSegmentId = 1;
        _logger = logger ?? NullLogger.Instance;
        _stream = OpenSegment(_currentSegmentId);
        CurrentSize = _stream.Length;
        Log.WalOpenedWriter(_logger, _dir, _segmentSizeLimit);
    }

    public ulong LastSequence { get; private set; }
    public long CurrentSize { get; private set; }

    public ValueTask BeginTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            EnsureCapacity(1 + 8);
            WriteHeader(WalConstants.RecordBeginTxn, txnId, false);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AppendAsync(ulong txnId, DbEntry entry, CancellationToken ct = default)
    {
        using var _ = TelemetryHelper.StartActivityScope(TelemetrySources.ActivitySource, _logger,
            "WAL.Append", ActivityKind.Internal,
            new KeyValuePair<string, object?>("txn.id", txnId),
            new KeyValuePair<string, object?>("entry.kind", entry.Kind.ToString()));

        WalAppended(entry);

        lock (_writeLock)
        {
            var seq = entry.Sequence == 0 ? LastSequence + 1 : entry.Sequence;
            if (seq <= LastSequence) seq = LastSequence + 1; // enforce monotonic
            LastSequence = seq;

            int valueLen;
            var writeValue = false;
            switch (entry.Kind)
            {
                case DbEntryKind.Put:
                    valueLen = entry.Value.Length;
                    writeValue = valueLen > 0;
                    break;
                case DbEntryKind.DeleteKey:
                    valueLen = -1; // marker
                    break;
                case DbEntryKind.DeleteRange:
                    valueLen = entry.Value.Length; // end key
                    writeValue = valueLen > 0;
                    break;
                default:
                    valueLen = entry.Value.Length;
                    writeValue = valueLen > 0;
                    break;
            }

            // header lengths
            const int recordHeaderLen = 1 + 8 + 8; // type + txn + seq (for RecordEntry)
            const int entryMetaLen = 1 + 4 + 4; // kind + keyLen + valLen
            var keyLen = entry.Key.Length;
            var payloadLen = valueLen > 0 ? valueLen : 0;
            var recordSize = recordHeaderLen + entryMetaLen + keyLen + payloadLen;

            EnsureCapacity(recordSize);

            // Rent a buffer via Buf helper
            using var buf = Buf.Rent(recordSize);
            var span = buf.Span[..recordSize];
            // record header
            span[0] = WalConstants.RecordEntry;
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(1, 8), txnId);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(9, 8), seq);

            // entry meta
            var pos = recordHeaderLen;
            span[pos] = (byte)entry.Kind;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 1, 4), keyLen);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 5, 4), valueLen);
            pos += entryMetaLen;

            // key
            entry.Key.Span.CopyTo(span.Slice(pos, keyLen));
            pos += keyLen;

            // value if present
            if (writeValue)
                entry.Value.Span.CopyTo(span.Slice(pos, payloadLen));

            // write once
            _stream.Write(span);

            CurrentSize += recordSize;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CommitTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            EnsureCapacity(1 + 8);
            WriteHeader(WalConstants.RecordCommitTxn, txnId, false);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackTransactionAsync(ulong txnId, CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            EnsureCapacity(1 + 8);
            WriteHeader(WalConstants.RecordRollbackTxn, txnId, false);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        lock (_writeLock)
        {
            _stream.Flush(true);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync();
        await _stream.DisposeAsync();
        Log.WalClosedWriter(_logger, _dir, LastSequence);
    }

    void EnsureCapacity(int nextRecordSize)
    {
        if (CurrentSize + nextRecordSize > _segmentSizeLimit) RollSegment();
    }

    void RollSegment()
    {
        _stream.Flush(true);
        _stream.Dispose();
        _currentSegmentId++;
        _stream = OpenSegment(_currentSegmentId);
        CurrentSize = 0;
        Log.WalRolledSegment(_logger, _currentSegmentId);
    }

    FileStream OpenSegment(ulong segmentId)
    {
        var path = Path.Combine(_dir, $"{segmentId:D20}.wal");
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    void WriteHeader(byte type, ulong txnId, bool includeSequence, ulong seq = 0)
    {
        if (includeSequence)
        {
            Span<byte> header = stackalloc byte[1 + 8 + 8];
            header[0] = type;
            BinaryPrimitives.WriteUInt64LittleEndian(header[1..9], txnId);
            BinaryPrimitives.WriteUInt64LittleEndian(header[9..17], seq);
            _stream.Write(header);
            CurrentSize += header.Length;
        }
        else
        {
            Span<byte> header = stackalloc byte[1 + 8];
            header[0] = type;
            BinaryPrimitives.WriteUInt64LittleEndian(header[1..9], txnId);
            _stream.Write(header);
            CurrentSize += header.Length;
        }
    }

    static void WalAppended(DbEntry entry)
    {
        TelemetrySources.WalAppends.Add(1, new KeyValuePair<string, object?>("kind", entry.Kind.ToString()));
        TelemetrySources.WalAppendSize.Record(entry.Key.Length + entry.Value.Length,
            new KeyValuePair<string, object?>("kind", entry.Kind.ToString()));
    }
}