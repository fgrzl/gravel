# Actor-Based LSM with Cloud-Native WAL and SST

This document describes the new actor-based architecture for Gravel, designed for deterministic, embeddable database operations with cloud-native storage support.

## Architecture Overview

### Core Principles

1. **Actor Model**: All background operations (flush, compaction, WAL upload, manifest sync, eviction) are orchestrated by a single central actor runtime that sequences every action deterministically.

2. **Unified Write Path**: Operations follow a straight pipeline:
   - Operation ? Sequence Number ? WAL Append ? Memtable Apply ? Signal Flush ? Done
   - No random worker threads mutating engine state

3. **Cloud-Native Storage**: 
   - WAL: Local fast buffer with async cloud durability
   - SST: Primary residence in cloud with optional local NVMe cache
   - Manifest: Orchestrates recovery and compaction decisions

4. **Deterministic Compaction**: Plans and executes compaction as intent log entries with same input ? same sequence every time

5. **Embeddable**: Library embedded in application process; no separate server or RPC layer

## New Namespaces

### `Gravel.Actor`
Central actor runtime and messaging infrastructure:
- `ActorMessage`: Base class for all actor messages
- `IActorRuntime`: Interface for the actor runtime
- `ActorRuntime`: Default implementation with FIFO message/task processing
- `IActorTask`: Interface for background tasks
- `ActorIntentLogEntry`: Records intent log for determinism and recovery

### `Gravel.Actor.Messages`
Message types for actor communication:
- `FlushMemTableMessage`: Trigger memtable flush to SST
- `ScheduleCompactionMessage`: Schedule compaction passes
- `UploadWalSegmentMessage`: Upload WAL segment to cloud
- `EvictSstMessage`: Evict SST from local cache
- `SyncManifestMessage`: Sync manifest to persistent storage

### `Gravel.Actor.Tasks`
Concrete task implementations:
- `FlushMemTableTask`: Executes memtable flush
- `CompactionTask`: Executes compaction pass with retry logic
- `UploadWalSegmentTask`: Uploads WAL with retries
- `EvictSstTask`: Evicts SST files
- `SyncManifestTask`: Persists manifest with retries

### `Gravel.Cloud.Abstractions`
Cloud storage interfaces and manifest management:
- `ICloudStorage`: Generic cloud object storage (Azure, S3, Wasabi, etc.)
- `ICloudWalManager`: Cloud-native WAL with async uploads
- `ICloudSstManager`: SST files in cloud with local caching
- `IManifestManager`: Manifest with compaction history logging
- Supporting types: `WalSegmentInfo`, `ManifestData`, `SstFileInfo`, `CompactionAction`

### `Gravel.Cloud.NoOp`
No-op implementations for local-only deployments:
- `NoOpCloudStorage`: Stub cloud storage
- `NoOpCloudWalManager`: Local-only WAL manager
- `NoOpCloudSstManager`: Local-only SST manager
- `NoOpManifestManager`: In-memory manifest manager

## How the Actor Runtime Works

The `ActorRuntime` maintains two channels:
1. **Message Queue**: For lifecycle events (flush, compaction scheduling, etc.)
2. **Task Queue**: For background work (actual flush, compaction execution, uploads)

The dispatcher loop processes messages and tasks in strict FIFO order:
```
while (not cancelled) {
  - Read one message (if available) ? LogMessage
  - Read one task (if available) ? ExecuteTask with retries
}
```

This ensures:
- **Determinism**: Same sequence of inputs ? same sequence of tasks
- **Predictability**: No race conditions in engine state mutations
- **Traceability**: Intent log records every decision

## Manifest and Compaction Intent Log

The manifest manager maintains a log of compaction actions, enabling:
- **Recovery**: Replay compaction history to rebuild state
- **Determinism**: Given same compaction decisions, produce same file structure
- **Debugging**: Audit trail of all compaction operations

A `CompactionAction` records:
- From/to levels
- Input and output file paths
- Start time and status (pending/in-progress/completed/failed)

## Cloud Storage Abstractions

### ICloudStorage
Generic blob storage with methods:
- `ExistsAsync(path)`
- `UploadAsync(path, stream, metadata)`
- `DownloadAsync(path, stream)`
- `ListAsync(prefix)`
- `DeleteAsync(path)`
- `GetSizeAsync(path)`

### ICloudWalManager
Decouples local WAL buffering from cloud durability:
- Local segment appends are fast (memory-only)
- `FlushSegmentAsync()` uploads to cloud asynchronously
- Recovery reads from manifest + WAL segments (local + cloud)
- Tracks segment metadata: start/end sequences, size, cloud durability

### ICloudSstManager
Treats SST files as cloud-resident with optional local cache:
- `WriteAsync()`: Write SST directly to cloud, optionally cache locally
- `ReadAsync()`: Read from local cache if available, else cloud
- `PinAsync()`: Keep file in cache (prevents eviction)
- `UnpinAsync()`: Allow eviction
- `PrefetchAsync()`: Proactively download to cache
- `EvictAsync()`: Remove from cache (keeps cloud copy)
- `GetCacheStats()`: Monitor cache usage

## No-Op Implementations

For local-only deployments that don't need cloud features, use the no-op implementations:

```csharp
var walManager = new NoOpCloudWalManager();
var sstManager = new NoOpCloudSstManager();
var manifestManager = new NoOpManifestManager();
```

These stubs allow the API to work without actual cloud I/O while maintaining compatibility with cloud-aware code.

## Integration Points

### With DbEngine

The existing `DbEngine` can be extended to:
1. Create an `ActorRuntime` instance
2. Post `FlushMemTableMessage` when memtable threshold reached
3. Post `ScheduleCompactionMessage` when level threshold exceeded
4. Post `UploadWalSegmentMessage` when WAL segment fills
5. Post `SyncManifestMessage` after significant state changes
6. Post `EvictSstMessage` when cache is full

### Message Flow Example

```
User calls PutAsync()
  ? CommitSingleAsync() in write path
  ? Append to memtable
  ? If memtable > threshold:
      - Post FlushMemTableMessage
      - Actor dequeues message
      - Enqueues FlushMemTableTask
      - Task executes (writes SST, uploads to cloud)
      - On completion, possibly posts ScheduleCompactionMessage
```

## Determinism and Reproducibility

Same inputs ? Same sequence of actor messages ? Same sequence of tasks ? Same database state

This enables:
- **Testing**: Record input workload, replay, compare outputs
- **Debugging**: Inspect intent log to see decisions
- **Auditing**: Understand exact sequence of operations
- **Recovery**: Replay compaction history from manifest

## Performance Characteristics

- **Write Path**: No async I/O blocking (memtable append is synchronous, flush is background)
- **Read Path**: Memtable + cached SSTs (cloud misses are async prefetch)
- **Compaction**: Scheduled as background tasks with bandwidth throttling
- **WAL**: Local append is fast, cloud sync is async
- **Manifest**: Typically small, sync is quick but can be retried

## Example Usage (Future)

```csharp
// Create runtime
var logger = loggerFactory.CreateLogger<ActorRuntime>();
var runtime = new ActorRuntime(logger);

// Create cloud managers (or use no-ops for local-only)
var cloudStorage = new S3CloudStorage(awsOptions);
var walManager = new CloudWalManager(cloudStorage, logger);
var sstManager = new CloudSstManager(cloudStorage, logger);
var manifestManager = new CloudManifestManager(cloudStorage, logger);

// Pass to engine (future integration)
var engine = new DbEngine(
    options,
    walFactory,
    sstFactory,
    compactionWorker,
    runtime,
    walManager,
    sstManager,
    manifestManager,
    logger
);
```

## Testing Strategy

1. **Unit Tests**: Test individual tasks and managers in isolation
2. **Integration Tests**: Test actor runtime with messages and tasks
3. **Determinism Tests**: Run same workload twice, verify same intent log
4. **Cloud Tests**: Mock cloud storage, verify upload/download logic
5. **Failure Tests**: Simulate task failures, verify retry logic

## Future Enhancements

- **Adaptive Compaction**: Actor adjusts compaction strategy based on workload
- **Multi-Tenancy**: Separate actors per tenant database
- **Replication**: Actor broadcasts changes to replicas
- **Consensus**: Use actor for distributed decision-making
- **Analytics**: Export intent log for workload analysis
