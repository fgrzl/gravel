using Microsoft.Extensions.Logging;

namespace Gravel.Logging;

/// <summary>
///     Source-generated logging helpers for Gravel components.
///     Each method corresponds to a structured log event with a unique event ID and message template.
/// </summary>
public static partial class Log
{
    /// <summary>
    ///     Logs when the GravelDb is opening at the specified path.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The database path.</param>
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Opening GravelDb at {Path}")]
    public static partial void DbOpening(ILogger logger, string path);

    /// <summary>
    ///     Logs the number of WAL entries replayed.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">The number of WAL entries.</param>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Replayed {Count} WAL entries")]
    public static partial void WalReplayed(ILogger logger, int count);

    /// <summary>
    ///     Logs when a record is appended with a specific sequence number.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="seq">The sequence number.</param>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Appended record seq {Seq}")]
    public static partial void PutSeq(ILogger logger, ulong seq);

    /// <summary>
    ///     Logs when the DbEngine is created with configuration details.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dbPath">The database path.</param>
    /// <param name="walDir">The WAL directory.</param>
    /// <param name="sstDir">The SST directory.</param>
    /// <param name="levels">The number of SST levels.</param>
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "DbEngine created. DBPath={DbPath} WAL={WalDir} SST={SstDir} Levels={Levels}")]
    public static partial void EngineCreated(ILogger logger, string dbPath, string walDir, string sstDir, int levels);

    /// <summary>
    ///     Logs when an SST file is loaded for a specific level and path.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="level">The SST level.</param>
    /// <param name="path">The SST file path.</param>
    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Loaded SST file level={Level} path='{Path}'")]
    public static partial void SstLoaded(ILogger logger, int level, string path);

    /// <summary>
    ///     Logs when loading an SST file fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The SST file path.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "SST load failed for '{Path}': {Error}")]
    public static partial void SstLoadFailed(ILogger logger, string path, string error);

    /// <summary>
    ///     Logs detailed information after WAL replay completes.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="entries">The number of entries replayed.</param>
    /// <param name="memCount">The number of MemTable entries.</param>
    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug,
        Message = "WAL replay complete. Entries replayed={Entries} MemTableCount={MemCount}")]
    public static partial void WalReplayedDetailed(ILogger logger, int entries, int memCount);

    /// <summary>
    ///     Logs when PutAsync is called with the key length.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="keyLen">The key length.</param>
    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "PutAsync called. KeyLength={KeyLen}")]
    public static partial void PutCalled(ILogger logger, int keyLen);

    /// <summary>
    ///     Logs when InsertAsync is called with the key length.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="keyLen">The key length.</param>
    [LoggerMessage(EventId = 2005, Level = LogLevel.Debug, Message = "InsertAsync called. KeyLength={KeyLen}")]
    public static partial void InsertCalled(ILogger logger, int keyLen);

    /// <summary>
    ///     Logs when GetAsync finds a key in SST.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="keyLen">The key length.</param>
    [LoggerMessage(EventId = 2006, Level = LogLevel.Debug, Message = "GetAsync found key in SST. KeyLength={KeyLen}")]
    public static partial void GetHitSst(ILogger logger, int keyLen);

    /// <summary>
    ///     Logs when GetAsync misses a key.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="keyLen">The key length.</param>
    [LoggerMessage(EventId = 2007, Level = LogLevel.Debug, Message = "GetAsync miss. KeyLength={KeyLen}")]
    public static partial void GetMiss(ILogger logger, int keyLen);

    /// <summary>
    ///     Logs when DeleteAsync is called with the key length.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="keyLen">The key length.</param>
    [LoggerMessage(EventId = 2008, Level = LogLevel.Debug, Message = "DeleteAsync called. KeyLength={KeyLen}")]
    public static partial void DeleteCalled(ILogger logger, int keyLen);

    /// <summary>
    ///     Logs when DeleteRangeAsync is called with start and end key lengths.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="startLen">The start key length.</param>
    /// <param name="endLen">The end key length.</param>
    [LoggerMessage(EventId = 2009, Level = LogLevel.Debug,
        Message = "DeleteRangeAsync called. StartLen={StartLen} EndLen={EndLen}")]
    public static partial void DeleteRangeCalled(ILogger logger, int startLen, int endLen);

    /// <summary>
    ///     Logs when BatchAsync is called with the number of mutations.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">The number of mutations.</param>
    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug, Message = "BatchAsync called. Mutations={Count}")]
    public static partial void BatchCalled(ILogger logger, int count);

    /// <summary>
    ///     Logs when ScanAsync is called with start/end key lengths and limit.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="startLen">The start key length.</param>
    /// <param name="endLen">The end key length.</param>
    /// <param name="limit">The scan limit.</param>
    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug,
        Message = "ScanAsync called. StartLen={StartLen} EndLen={EndLen} Limit={Limit}")]
    public static partial void ScanCalled(ILogger logger, int? startLen, int? endLen, int? limit);

    /// <summary>
    ///     Logs when Scan yields a key of a specific length.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="keyLen">The key length.</param>
    [LoggerMessage(EventId = 2012, Level = LogLevel.Trace, Message = "Scan yielding key length={KeyLen}")]
    public static partial void ScanYielding(ILogger logger, int keyLen);

    /// <summary>
    ///     Logs the creation of a transaction with its ID and initial sequence number.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="beginSeq">The initial sequence number for the transaction.</param>
    [LoggerMessage(EventId = 2013, Level = LogLevel.Debug,
        Message = "BeginTransaction created txn {TxnId} beginSeq={BeginSeq}")]
    public static partial void BeginTransactionCreated(ILogger logger, ulong txnId, ulong beginSeq);

    /// <summary>
    ///     Logs when a transaction is about to be committed, including its ID and the number of staged operations.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="count">The number of staged operations.</param>
    [LoggerMessage(EventId = 2014, Level = LogLevel.Information,
        Message = "Committing transaction {TxnId} with {Count} staged operations")]
    public static partial void TransactionCommitting(ILogger logger, ulong txnId, int count);

    /// <summary>
    ///     Logs after a transaction has been committed, with its ID and the commit sequence number.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="commitSeq">The sequence number at which the transaction was committed.</param>
    [LoggerMessage(EventId = 2015, Level = LogLevel.Debug,
        Message = "Transaction {TxnId} committed at seq {CommitSeq}")]
    public static partial void TransactionCommitted(ILogger logger, ulong txnId, ulong commitSeq);

    /// <summary>
    ///     Logs when committing a transaction fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 2016, Level = LogLevel.Error,
        Message = "CommitTransactionAsync failed for txn {TxnId}: {Error}")]
    public static partial void TransactionCommitFailed(ILogger logger, ulong txnId, string error);

    /// <summary>
    ///     Logs the start of a single operation commit within a transaction.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="op">The operation being committed.</param>
    /// <param name="keyLen">The key length.</param>
    [LoggerMessage(EventId = 2017, Level = LogLevel.Debug,
        Message = "CommitSingleAsync starting txn {TxnId} op={Op} KeyLen={KeyLen}")]
    public static partial void SingleCommitStarting(ILogger logger, ulong txnId, object op, int keyLen);

    /// <summary>
    ///     Logs after a single operation commit has completed.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="op">The committed operation.</param>
    /// <param name="seq">The sequence number of the committed operation.</param>
    [LoggerMessage(EventId = 2018, Level = LogLevel.Debug,
        Message = "CommitSingleAsync committed txn {TxnId} op={Op} seq={Seq}")]
    public static partial void SingleCommitCommitted(ILogger logger, ulong txnId, object op, ulong seq);

    /// <summary>
    ///     Logs the committing of a batch of mutations within a transaction.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    [LoggerMessage(EventId = 2019, Level = LogLevel.Debug, Message = "Committed mutations batch txn {TxnId}")]
    public static partial void MutationsBatchCommitted(ILogger logger, ulong txnId);

    /// <summary>
    ///     Logs when a flush is triggered, including the current memtable count and the threshold.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">The current memtable count.</param>
    /// <param name="threshold">The flush threshold.</param>
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug,
        Message = "Triggering flush: memtable count={Count}, threshold={Threshold}")]
    public static partial void FlushTrigger(ILogger logger, int count, int threshold);

    /// <summary>
    ///     Logs when a flush is enqueued with the number of entries.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">The number of entries to be flushed.</param>
    [LoggerMessage(EventId = 3001, Level = LogLevel.Debug, Message = "Enqueued flush with {Count} entries")]
    public static partial void FlushEnqueued(ILogger logger, int count);

    /// <summary>
    ///     Logs the creation of an SST file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The path of the created SST file.</param>
    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "Created SST '{Path}'")]
    public static partial void SstCreated(ILogger logger, string path);

    /// <summary>
    ///     Logs the completion of a flush operation, including the path of the new SST file and its sequence tag.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The path of the new SST file.</param>
    /// <param name="seqTag">The sequence tag of the new SST file.</param>
    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug,
        Message = "Flush complete. New SST added to L0 path='{Path}' seqTag={SeqTag}")]
    public static partial void FlushComplete(ILogger logger, string path, ulong seqTag);

    /// <summary>
    ///     Logs when a compaction pass is scheduled.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Scheduled compaction pass")]
    public static partial void CompactionScheduled(ILogger logger);

    /// <summary>
    ///     Logs the start of a compaction process, including the level and number of files involved.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="level">The level being compacted.</param>
    /// <param name="fileCount">The number of files being compacted.</param>
    /// <param name="next">The next level to be compacted to.</param>
    [LoggerMessage(EventId = 3005, Level = LogLevel.Debug,
        Message = "Compaction starting. Level={Level} Files={FileCount} -> L{Next}")]
    public static partial void CompactionStarted(ILogger logger, int level, int fileCount, int next);

    /// <summary>
    ///     Logs the completion of a compaction process, including the output path of the compacted SST file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="outPath">The output path of the compacted SST file.</param>
    [LoggerMessage(EventId = 3006, Level = LogLevel.Debug, Message = "Compaction finished. Output='{OutPath}'")]
    public static partial void CompactionFinished(ILogger logger, string outPath);

    /// <summary>
    ///     Logs an error that occurs during compaction.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "Compaction error: {Error}")]
    public static partial void CompactionError(ILogger logger, string error);

    /// <summary>
    ///     Logs a warning when disposing of an SST reader fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The path of the SST file.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning,
        Message = "Error disposing reader for SST '{Path}': {Error}")]
    public static partial void SstDisposeError(ILogger logger, string path, string error);

    /// <summary>
    ///     Logs a warning when a transaction rollback fails during a failure recovery.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="txnId">The transaction ID.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning,
        Message = "Rollback of txn {TxnId} during failure also failed: {Error}")]
    public static partial void RollbackFailed(ILogger logger, ulong txnId, string error);

    /// <summary>
    ///     Logs a warning when deleting an old SST file fails after a compaction.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The path of the SST file.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 3010, Level = LogLevel.Warning,
        Message = "Failed to delete old SST '{Path}' after compaction: {Error}")]
    public static partial void SstDeleteFailed(ILogger logger, string path, string error);

    /// <summary>
    ///     Logs a warning when a WAL flush fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning, Message = "WAL flush failed: {Error}")]
    public static partial void WalFlushError(ILogger logger, string error);

    /// <summary>
    ///     Logs a warning when compactor recovery fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Compactor recovery failed: {Error}")]
    public static partial void CompactorRecoveryError(ILogger logger, string error);

    /// <summary>
    ///     Logs debugging information when the GravelDb is disposed.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    [LoggerMessage(EventId = 4002, Level = LogLevel.Debug, Message = "Disposed GravelDb")]
    public static partial void Disposed(ILogger logger);

    /// <summary>
    ///     Logs when an SST file is opened for reading, including the buffer size.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The path of the SST file.</param>
    /// <param name="bufferSize">The buffer size used for reading.</param>
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information,
        Message = "Opened SST for read {Path} (buf={BufferSize})")]
    public static partial void SstOpenedRead(ILogger logger, string path, int bufferSize);

    /// <summary>
    ///     Logs the number of index entries loaded from an SST file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">The number of index entries.</param>
    /// <param name="path">The path of the SST file.</param>
    [LoggerMessage(EventId = 5001, Level = LogLevel.Debug, Message = "SST index entries {Count} for {Path}")]
    public static partial void SstIndexLoaded(ILogger logger, int count, string path);

    /// <summary>
    ///     Logs the details of the bloom filter loaded from an SST file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="bits">The number of bits in the bloom filter.</param>
    /// <param name="hashFunctions">The number of hash functions used.</param>
    /// <param name="bytes">The size of the bloom filter in bytes.</param>
    /// <param name="path">The path of the SST file.</param>
    [LoggerMessage(EventId = 5002, Level = LogLevel.Debug,
        Message = "SST bloom loaded bits={Bits} k={HashFunctions} bytes={Bytes} for {Path}")]
    public static partial void SstBloomLoaded(ILogger logger, int bits, int hashFunctions, int bytes, string path);

    /// <summary>
    ///     Logs the minimum and maximum keys loaded from an SST file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="minLen">The length of the minimum key.</param>
    /// <param name="maxLen">The length of the maximum key.</param>
    /// <param name="path">The path of the SST file.</param>
    [LoggerMessage(EventId = 5003, Level = LogLevel.Debug,
        Message = "SST min/max loaded min={MinLen} max={MaxLen} for {Path}")]
    public static partial void SstMinMaxLoaded(ILogger logger, int minLen, int maxLen, string path);

    /// <summary>
    ///     Logs when a key is filtered out by the bloom filter for an SST file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="keyLen">The length of the filtered key.</param>
    /// <param name="path">The path of the SST file.</param>
    [LoggerMessage(EventId = 5004, Level = LogLevel.Debug,
        Message = "Bloom filtered key len {KeyLen} for {Path}")]
    public static partial void SstBloomFiltered(ILogger logger, int keyLen, string path);

    /// <summary>
    ///     Logs when an SST file is opened for writing, including details about the temp file and buffer size.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="finalPath">The final path of the SST file.</param>
    /// <param name="tempPath">The temp path of the SST file.</param>
    /// <param name="bufferSize">The buffer size used for writing.</param>
    /// <param name="sparse">Indicates if sparse writing is enabled.</param>
    [LoggerMessage(EventId = 5010, Level = LogLevel.Information,
        Message = "Opened SST for write {FinalPath} temp {TempPath} (buf={BufferSize}, sparse={Sparse})")]
    public static partial void SstOpenedWrite(
        ILogger logger, string finalPath, string tempPath, int bufferSize,
        int sparse);

    /// <summary>
    ///     Logs the completion of writing an SST temp file, including the number of entries and index count.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tempPath">The temp path of the SST file.</param>
    /// <param name="entries">The number of entries written.</param>
    /// <param name="indexCount">The number of index entries.</param>
    [LoggerMessage(EventId = 5011, Level = LogLevel.Information,
        Message = "Finished writing SST temp {TempPath} entries={Entries} idx={IndexCount}")]
    public static partial void SstWriteFinished(ILogger logger, string tempPath, int entries, int indexCount);

    /// <summary>
    ///     Logs the flushing of an SST temp file, including details about the index and bloom offsets, and min/max keys.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tempPath">The temp path of the SST file.</param>
    /// <param name="indexOffset">The index offset in the SST file.</param>
    /// <param name="bloomOffset">The bloom filter offset in the SST file.</param>
    /// <param name="minLen">The minimum key length.</param>
    /// <param name="maxLen">The maximum key length.</param>
    [LoggerMessage(EventId = 5012, Level = LogLevel.Information,
        Message =
            "Flushed SST temp {TempPath} idxOffset={IndexOffset} bloomOffset={BloomOffset} min={MinLen} max={MaxLen}")]
    public static partial void SstFlushed(
        ILogger logger, string tempPath, long indexOffset, long bloomOffset,
        int minLen, int maxLen);

    /// <summary>
    ///     Logs the sealing of an SST file from a temp file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="finalPath">The final path of the sealed SST file.</param>
    /// <param name="tempPath">The temp path of the SST file.</param>
    [LoggerMessage(EventId = 5013, Level = LogLevel.Information,
        Message = "Sealed SST {FinalPath} from temp {TempPath}")]
    public static partial void SstSealed(ILogger logger, string finalPath, string tempPath);

    /// <summary>
    ///     Logs a warning when a flush is attempted on a disposed SST writer.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tempPath">The temp path of the disposed SST writer.</param>
    [LoggerMessage(EventId = 5014, Level = LogLevel.Warning,
        Message = "SST flush attempted on disposed writer temp {TempPath}")]
    public static partial void SstFlushFailedDisposed(ILogger logger, string tempPath);

    /// <summary>
    ///     Logs a warning when flushing an SST temp file fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tempPath">The temp path of the SST file.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 5015, Level = LogLevel.Warning, Message = "SST flush failed for temp {TempPath}: {Error}")]
    public static partial void SstFlushFailed(ILogger logger, string tempPath, string error);

    /// <summary>
    ///     Logs a warning when sealing an SST file from a temp file fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="finalPath">The final path of the SST file.</param>
    /// <param name="tempPath">The temp path of the SST file.</param>
    /// <param name="error">The error message.</param>
    [LoggerMessage(EventId = 5016, Level = LogLevel.Warning,
        Message = "Failed to seal SST final {FinalPath} from temp {TempPath}: {Error}")]
    public static partial void SstSealFailed(ILogger logger, string finalPath, string tempPath, string error);

    /// <summary>
    ///     Logs when there are no WAL segments to replay in a directory.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dir">The directory path.</param>
    [LoggerMessage(EventId = 5020, Level = LogLevel.Information,
        Message = "No WAL segments to replay in {Dir}")]
    public static partial void WalNoSegments(ILogger logger, string dir);

    /// <summary>
    ///     Logs the number of WAL segments being replayed in a directory.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">The number of WAL segments.</param>
    /// <param name="dir">The directory path.</param>
    [LoggerMessage(EventId = 5021, Level = LogLevel.Information,
        Message = "Replaying {Count} WAL segments in {Dir}")]
    public static partial void WalReplayingSegments(ILogger logger, int count, string dir);

    /// <summary>
    ///     Logs a warning for an unknown WAL record type encountered during replay.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="type">The unknown record type.</param>
    /// <param name="file">The WAL file being processed.</param>
    [LoggerMessage(EventId = 5022, Level = LogLevel.Warning,
        Message = "Unknown WAL record type {Type} in {File}; stopping replay")]
    public static partial void WalUnknownRecordType(ILogger logger, int type, string file);

    /// <summary>
    ///     Logs the number of records replayed from a WAL file.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">The number of records replayed.</param>
    /// <param name="file">The WAL file path.</param>
    [LoggerMessage(EventId = 5023, Level = LogLevel.Information,
        Message = "Replayed {Count} records from {File}")]
    public static partial void WalFileReplayed(ILogger logger, int count, string file);

    /// <summary>
    ///     Logs when a WAL writer is opened in a directory, including the segment limit.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dir">The directory path.</param>
    /// <param name="limit">The segment limit.</param>
    [LoggerMessage(EventId = 5030, Level = LogLevel.Information,
        Message = "Opened WAL writer in {Dir} (segmentLimit={Limit})")]
    public static partial void WalOpenedWriter(ILogger logger, string dir, long limit);

    /// <summary>
    ///     Logs when a WAL writer is closed in a directory, including the last sequence number.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dir">The directory path.</param>
    /// <param name="lastSequence">The last sequence number written.</param>
    [LoggerMessage(EventId = 5031, Level = LogLevel.Information,
        Message = "Closed WAL writer in {Dir} lastSeq={LastSequence}")]
    public static partial void WalClosedWriter(ILogger logger, string dir, ulong lastSequence);

    /// <summary>
    ///     Logs the rolling of a WAL segment to a new ID.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="id">The new segment ID.</param>
    [LoggerMessage(EventId = 5032, Level = LogLevel.Information,
        Message = "Rolled WAL segment to {Id:D20}")]
    public static partial void WalRolledSegment(ILogger logger, ulong id);
}
