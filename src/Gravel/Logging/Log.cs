using Microsoft.Extensions.Logging;

namespace Gravel.Logging;

/// <summary>
///     Source-generated logging helpers for Gravel components.
/// </summary>
public static partial class Log
{
    // Core database lifecycle (1000-1999)
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Opening GravelDb at {Path}")]
    public static partial void DbOpening(ILogger logger, string path);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Replayed {Count} WAL entries")]
    public static partial void WalReplayed(ILogger logger, int count);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Appended record seq {Seq}")]
    public static partial void PutSeq(ILogger logger, ulong seq);

    // Engine / operational events (2000-2999)
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Engine created. DBPath={DbPath} WAL={WalDir} SST={SstDir} Levels={Levels}")]
    public static partial void EngineCreated(ILogger logger, string dbPath, string walDir, string sstDir, int levels);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Loaded SST file level={Level} path='{Path}'")]
    public static partial void SstLoaded(ILogger logger, int level, string path);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "SST load failed for '{Path}': {Error}")]
    public static partial void SstLoadFailed(ILogger logger, string path, string error);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug,
        Message = "WAL replay complete. Entries replayed={Entries} MemTableCount={MemCount}")]
    public static partial void WalReplayedDetailed(ILogger logger, int entries, int memCount);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "PutAsync called. KeyLength={KeyLen}")]
    public static partial void PutCalled(ILogger logger, int keyLen);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Debug, Message = "InsertAsync called. KeyLength={KeyLen}")]
    public static partial void InsertCalled(ILogger logger, int keyLen);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Debug, Message = "GetAsync found key in SST. KeyLength={KeyLen}")]
    public static partial void GetHitSst(ILogger logger, int keyLen);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Debug, Message = "GetAsync miss. KeyLength={KeyLen}")]
    public static partial void GetMiss(ILogger logger, int keyLen);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Debug, Message = "DeleteAsync called. KeyLength={KeyLen}")]
    public static partial void DeleteCalled(ILogger logger, int keyLen);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Debug,
        Message = "DeleteRangeAsync called. StartLen={StartLen} EndLen={EndLen}")]
    public static partial void DeleteRangeCalled(ILogger logger, int startLen, int endLen);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug, Message = "BatchAsync called. Mutations={Count}")]
    public static partial void BatchCalled(ILogger logger, int count);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug,
        Message = "ScanAsync called. StartLen={StartLen} EndLen={EndLen} Limit={Limit}")]
    public static partial void ScanCalled(ILogger logger, int? startLen, int? endLen, int? limit);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Trace, Message = "Scan yielding key length={KeyLen}")]
    public static partial void ScanYielding(ILogger logger, int keyLen);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Debug,
        Message = "BeginTransaction created txn {TxnId} beginSeq={BeginSeq}")]
    public static partial void BeginTransactionCreated(ILogger logger, ulong txnId, ulong beginSeq);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Information,
        Message = "Committing transaction {TxnId} with {Count} staged operations")]
    public static partial void TransactionCommitting(ILogger logger, ulong txnId, int count);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Debug,
        Message = "Transaction {TxnId} committed at seq {CommitSeq}")]
    public static partial void TransactionCommitted(ILogger logger, ulong txnId, ulong commitSeq);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Error,
        Message = "CommitTransactionAsync failed for txn {TxnId}: {Error}")]
    public static partial void TransactionCommitFailed(ILogger logger, ulong txnId, string error);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Debug,
        Message = "CommitSingleAsync starting txn {TxnId} op={Op} KeyLen={KeyLen}")]
    public static partial void SingleCommitStarting(ILogger logger, ulong txnId, object op, int keyLen);

    [LoggerMessage(EventId = 2018, Level = LogLevel.Debug,
        Message = "CommitSingleAsync committed txn {TxnId} op={Op} seq={Seq}")]
    public static partial void SingleCommitCommitted(ILogger logger, ulong txnId, object op, ulong seq);

    [LoggerMessage(EventId = 2019, Level = LogLevel.Debug, Message = "Committed mutations batch txn {TxnId}")]
    public static partial void MutationsBatchCommitted(ILogger logger, ulong txnId);

    // Flush & compaction (3000-3999)
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug,
        Message = "Triggering flush: memtable count={Count}, threshold={Threshold}")]
    public static partial void FlushTrigger(ILogger logger, int count, int threshold);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Debug, Message = "Enqueued flush with {Count} entries")]
    public static partial void FlushEnqueued(ILogger logger, int count);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "Created SST '{Path}'")]
    public static partial void SstCreated(ILogger logger, string path);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug,
        Message = "Flush complete. New SST added to L0 path='{Path}' seqTag={SeqTag}")]
    public static partial void FlushComplete(ILogger logger, string path, ulong seqTag);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Scheduled compaction pass")]
    public static partial void CompactionScheduled(ILogger logger);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Debug,
        Message = "Compaction starting. Level={Level} Files={FileCount} -> L{Next}")]
    public static partial void CompactionStarted(ILogger logger, int level, int fileCount, int next);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Debug, Message = "Compaction finished. Output='{OutPath}'")]
    public static partial void CompactionFinished(ILogger logger, string outPath);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "Compaction error: {Error}")]
    public static partial void CompactionError(ILogger logger, string error);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning,
        Message = "Error disposing reader for SST '{Path}': {Error}")]
    public static partial void SstDisposeError(ILogger logger, string path, string error);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning,
        Message = "Rollback of txn {TxnId} during failure also failed: {Error}")]
    public static partial void RollbackFailed(ILogger logger, ulong txnId, string error);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Warning,
        Message = "Failed to delete old SST '{Path}' after compaction: {Error}")]
    public static partial void SstDeleteFailed(ILogger logger, string path, string error);

    // Warnings / errors (4000+)
    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning, Message = "WAL flush failed: {Error}")]
    public static partial void WalFlushError(ILogger logger, string error);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Compactor recovery failed: {Error}")]
    public static partial void CompactorRecoveryError(ILogger logger, string error);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Debug, Message = "Disposed GravelDb")]
    public static partial void Disposed(ILogger logger);

    // Storage IO (5000-5999)
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information,
        Message = "Opened SST for read {Path} (buf={BufferSize})")]
    public static partial void SstOpenedRead(ILogger logger, string path, int bufferSize);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Debug, Message = "SST index entries {Count} for {Path}")]
    public static partial void SstIndexLoaded(ILogger logger, int count, string path);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Debug,
        Message = "SST bloom loaded bits={Bits} k={HashFunctions} bytes={Bytes} for {Path}")]
    public static partial void SstBloomLoaded(ILogger logger, int bits, int hashFunctions, int bytes, string path);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Debug,
        Message = "SST min/max loaded min={MinLen} max={MaxLen} for {Path}")]
    public static partial void SstMinMaxLoaded(ILogger logger, int minLen, int maxLen, string path);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Debug,
        Message = "Bloom filtered key len {KeyLen} for {Path}")]
    public static partial void SstBloomFiltered(ILogger logger, int keyLen, string path);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Information,
        Message = "Opened SST for write {FinalPath} temp {TempPath} (buf={BufferSize}, sparse={Sparse})")]
    public static partial void SstOpenedWrite(ILogger logger, string finalPath, string tempPath, int bufferSize,
        int sparse);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Information,
        Message = "Finished writing SST temp {TempPath} entries={Entries} idx={IndexCount}")]
    public static partial void SstWriteFinished(ILogger logger, string tempPath, int entries, int indexCount);

    [LoggerMessage(EventId = 5012, Level = LogLevel.Information,
        Message =
            "Flushed SST temp {TempPath} idxOffset={IndexOffset} bloomOffset={BloomOffset} min={MinLen} max={MaxLen}")]
    public static partial void SstFlushed(ILogger logger, string tempPath, long indexOffset, long bloomOffset,
        int minLen, int maxLen);

    [LoggerMessage(EventId = 5013, Level = LogLevel.Information,
        Message = "Sealed SST {FinalPath} from temp {TempPath}")]
    public static partial void SstSealed(ILogger logger, string finalPath, string tempPath);

    // New warnings for Sst writer errors
    [LoggerMessage(EventId = 5014, Level = LogLevel.Warning,
        Message = "SST flush attempted on disposed writer temp {TempPath}")]
    public static partial void SstFlushFailedDisposed(ILogger logger, string tempPath);

    [LoggerMessage(EventId = 5015, Level = LogLevel.Warning, Message = "SST flush failed for temp {TempPath}: {Error}")]
    public static partial void SstFlushFailed(ILogger logger, string tempPath, string error);

    [LoggerMessage(EventId = 5016, Level = LogLevel.Warning,
        Message = "Failed to seal SST final {FinalPath} from temp {TempPath}: {Error}")]
    public static partial void SstSealFailed(ILogger logger, string finalPath, string tempPath, string error);

    [LoggerMessage(EventId = 5020, Level = LogLevel.Information,
        Message = "No WAL segments to replay in {Dir}")]
    public static partial void WalNoSegments(ILogger logger, string dir);

    [LoggerMessage(EventId = 5021, Level = LogLevel.Information,
        Message = "Replaying {Count} WAL segments in {Dir}")]
    public static partial void WalReplayingSegments(ILogger logger, int count, string dir);

    [LoggerMessage(EventId = 5022, Level = LogLevel.Warning,
        Message = "Unknown WAL record type {Type} in {File}; stopping replay")]
    public static partial void WalUnknownRecordType(ILogger logger, int type, string file);

    [LoggerMessage(EventId = 5023, Level = LogLevel.Information,
        Message = "Replayed {Count} records from {File}")]
    public static partial void WalFileReplayed(ILogger logger, int count, string file);

    [LoggerMessage(EventId = 5030, Level = LogLevel.Information,
        Message = "Opened WAL writer in {Dir} (segmentLimit={Limit})")]
    public static partial void WalOpenedWriter(ILogger logger, string dir, long limit);

    [LoggerMessage(EventId = 5031, Level = LogLevel.Information,
        Message = "Closed WAL writer in {Dir} lastSeq={LastSequence}")]
    public static partial void WalClosedWriter(ILogger logger, string dir, ulong lastSequence);

    [LoggerMessage(EventId = 5032, Level = LogLevel.Information,
        Message = "Rolled WAL segment to {Id:D20}")]
    public static partial void WalRolledSegment(ILogger logger, ulong id);
}