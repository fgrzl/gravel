using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gravel.Abstractions;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Logging;
using Gravel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Storage.FileSystem.Wal;

/// <summary>
/// File-based WAL reader that replays records from segment files on disk.
/// </summary>
/// <param name="directory">Directory containing WAL segment files.</param>
/// <param name="logger">Optional logger.</param>
public sealed class FileWalReader(string directory, ILogger? logger = null) : IWalReader
{
    readonly ILogger _logger = logger ?? NullLogger.Instance;
    readonly List<string> _segments = [.. Directory.EnumerateFiles(directory, "*.wal").OrderBy(f => f)];

    /// <summary>
    /// Disposes the reader. No resources to release at the moment.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Replays WAL records from the segment files in order.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async stream of <see cref="WalRecord"/> items.</returns>
    public async IAsyncEnumerable<WalRecord> ReplayAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_segments.Count == 0)
            Log.WalNoSegments(_logger, directory);
        else
            Log.WalReplayingSegments(_logger, _segments.Count, directory);

        foreach (var file in _segments)
        {
            using var act = TelemetryHelper.StartActivityScope(TelemetrySources.ActivitySource, _logger,
                "WAL.ReplayFile", ActivityKind.Internal,
                new KeyValuePair<string, object?>("wal.file", file));

            // Use FileShare.ReadWrite to allow replay while a writer holds the segment open for write.
            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var br = new BinaryReader(fs);
            var localReplayed = 0;
            while (fs.Position < fs.Length)
            {
                ct.ThrowIfCancellationRequested();
                int type;
                try
                {
                    type = br.ReadByte();
                }
                catch (EndOfStreamException)
                {
                    break;
                }

                switch (type)
                {
                    case WalConstants.RecordBeginTxn:
                        yield return WalRecord.Begin(br.ReadUInt64());
                        break;
                    case WalConstants.RecordCommitTxn:
                        yield return WalRecord.Commit(br.ReadUInt64());
                        break;
                    case WalConstants.RecordRollbackTxn:
                        yield return WalRecord.Rollback(br.ReadUInt64());
                        break;
                    case WalConstants.RecordEntry:
                    {
                        var txnId = br.ReadUInt64();
                        var seq = br.ReadUInt64();
                        var kind = (DbEntryKind)br.ReadByte();
                        var keyLen = br.ReadInt32();
                        var valLen = br.ReadInt32();
                        var key = br.ReadBytes(keyLen);
                        byte[]? value = null;
                        if (valLen > 0) value = br.ReadBytes(valLen);
                        var entry = kind switch
                        {
                            DbEntryKind.Put => DbEntry.Put(key, value ?? ReadOnlyMemory<byte>.Empty, seq),
                            DbEntryKind.DeleteKey => DbEntry.DeleteKey(key, seq),
                            DbEntryKind.DeleteRange => DbEntry.DeleteRange(key, value ?? [], seq),
                            _ => DbEntry.Put(key, value ?? ReadOnlyMemory<byte>.Empty, seq)
                        };
                        localReplayed++;
                        TelemetrySources.WalReplayed.Add(1);
                        yield return WalRecord.DbEntry(txnId, entry);
                        break;
                    }
                    default:
                        Log.WalUnknownRecordType(_logger, type, file);
                        yield break;
                }
            }

            // record per-file replayed
            TelemetrySources.WalReplayed.Add(localReplayed, new KeyValuePair<string, object?>("file", file));
            Log.WalFileReplayed(_logger, localReplayed, file);
        }
    }
}
