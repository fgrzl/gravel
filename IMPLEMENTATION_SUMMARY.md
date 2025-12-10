# Actor-Based LSM Implementation for Gravel - Summary

## Overview

Successfully implemented an actor-based Log-Structured Merge (LSM) database architecture with cloud-native WAL and SST persistence layers. This implementation focuses on **determinism**, **embeddability**, and **predictable background operations** through a central actor runtime that sequences all background work.

## What Was Implemented

### 1. Actor Runtime Core (`Gravel.Actor`)

**Files created:**
- `ActorMessage.cs` - Base class for all messages
- `IActorRuntime.cs` - Interface defining actor runtime contract
- `ActorRuntime.cs` - Concrete implementation with FIFO message/task processing
- `IActorTask.cs` - Interface for background tasks with retry support
- `ActorIntentLogEntry.cs` - Records intent decisions for debugging and recovery

**Key features:**
- Strict FIFO processing ensures determinism
- Automatic task retry logic with configurable attempts
- Intent log tracks all decisions for debugging and recovery
- Performance statistics (messages/tasks processed, execution time)
- Graceful shutdown with `WaitForQuiesceAsync()`

### 2. Actor Messages (`Gravel.Actor.Messages`)

Implemented specific message types for different operations:
- `FlushMemTableMessage` - Trigger memtable flush to SST
- `ScheduleCompactionMessage` - Schedule compaction passes
- `UploadWalSegmentMessage` - Upload WAL segment to cloud
- `EvictSstMessage` - Evict SST from local cache
- `SyncManifestMessage` - Sync manifest to persistent storage

### 3. Actor Tasks (`Gravel.Actor.Tasks`)

Concrete task implementations that execute background work:
- `FlushMemTableTask` - Executes memtable flush
- `CompactionTask` - Executes compaction with retry logic (up to 3 attempts)
- `UploadWalSegmentTask` - Uploads WAL with retry logic (up to 5 attempts)
- `EvictSstTask` - Evicts SST files from cache
- `SyncManifestTask` - Persists manifest with retry logic (up to 5 attempts)

### 4. Cloud Storage Abstractions (`Gravel.Cloud.Abstractions`)

**Interfaces:**
- `ICloudStorage` - Generic cloud object storage (pluggable backend: S3, Azure, Wasabi, etc.)
- `ICloudWalManager` - Cloud-native WAL with async uploads and recovery
- `ICloudSstManager` - SST files in cloud with optional local cache layer
- `IManifestManager` - Manifest tracking and compaction history logging

**Supporting types:**
- `WalSegmentInfo` - Metadata about WAL segments
- `ManifestData` - Database state snapshot (levels, files, sequences)
- `SstFileInfo` - SST file metadata
- `CompactionAction` - Logged compaction decision for replay

### 5. No-Op Implementations (`Gravel.Cloud.NoOp`)

Stub implementations for local-only deployments:
- `NoOpCloudStorage` - Does nothing, maintains API compatibility
- `NoOpCloudWalManager` - Tracks sequences but doesn't persist to cloud
- `NoOpCloudSstManager` - Generates paths but doesn't cache/persist
- `NoOpManifestManager` - Keeps manifest in memory only

These allow code to work without actual cloud I/O while maintaining API compatibility.

## Architecture Principles

### 1. Unified Write Path
```
Operation ? Sequence Number ? WAL Append ? Memtable Apply ? Signal Flush ? Done
```
No random worker threads mutate engine state. All background work goes through the actor.

### 2. Determinism
- Same input workload ? Same sequence of messages ? Same sequence of tasks ? Same state
- Enables testing, debugging, and reproducible failures
- Intent log records every decision

### 3. Cloud-Native Storage Decoupling
- **WAL**: Local fast appends with async cloud durability
- **SST**: Primary residence in cloud with optional local NVMe cache
- **Manifest**: Orchestrates recovery and compaction decisions
- Cloud operations don't block the write path

### 4. Actor Model
- Central runtime owns all engine state mutations
- All background operations posted as messages
- Strict FIFO processing prevents race conditions
- Enables future distributed/replicated scenarios

## Integration Points (Future)

The existing `DbEngine` can be extended to:

1. Create `ActorRuntime` instance
2. Post `FlushMemTableMessage` when memtable > threshold
3. Post `ScheduleCompactionMessage` when level > threshold
4. Post `UploadWalSegmentMessage` when WAL segment fills
5. Post `SyncManifestMessage` after state changes
6. Post `EvictSstMessage` when cache full

## File Structure

```
src/Gravel/
??? Actor/
?   ??? ActorMessage.cs
?   ??? ActorRuntime.cs
?   ??? ActorIntentLogEntry.cs
?   ??? IActorRuntime.cs
?   ??? IActorTask.cs
?   ??? Messages/
?   ?   ??? FlushMemTableMessage.cs
?   ?   ??? ScheduleCompactionMessage.cs
?   ?   ??? UploadWalSegmentMessage.cs
?   ?   ??? EvictSstMessage.cs
?   ?   ??? SyncManifestMessage.cs
?   ??? Tasks/
?       ??? FlushMemTableTask.cs
?       ??? CompactionTask.cs
?       ??? UploadWalSegmentTask.cs
?       ??? EvictSstTask.cs
?       ??? SyncManifestTask.cs
??? Cloud/
    ??? Abstractions/
    ?   ??? ICloudStorage.cs
    ?   ??? ICloudWalManager.cs
    ?   ??? ICloudSstManager.cs
    ?   ??? IManifestManager.cs
    ??? NoOp/
        ??? NoOpCloudStorage.cs
        ??? NoOpCloudWalManager.cs
        ??? NoOpCloudSstManager.cs
        ??? NoOpManifestManager.cs
```

## Key Design Decisions

1. **Async/await throughout** - Uses ValueTask for efficiency, CancellationToken for cancellation
2. **Pluggable cloud backends** - ICloudStorage abstraction allows S3, Azure Blob, Wasabi, etc.
3. **Retry logic per task** - Each task type has appropriate retry policy
4. **No-op defaults** - Applications work locally without cloud configuration
5. **XML documentation** - Comprehensive API documentation for all public members
6. **FIFO guarantee** - Channels enforce ordering for determinism

## Example Usage (Conceptual)

```csharp
// Create actor runtime
var runtime = new ActorRuntime(logger);

// Use no-ops for local-only deployment
var walManager = new NoOpCloudWalManager();
var sstManager = new NoOpCloudSstManager();
var manifestManager = new NoOpManifestManager();

// Or use real cloud implementations for prod
// var cloudStorage = new S3CloudStorage(awsOptions);
// var walManager = new CloudWalManager(cloudStorage, ...);

// Future: Pass to DbEngine for integration
// The engine posts messages when needed:
await runtime.PostMessageAsync(new FlushMemTableMessage { ... });
await runtime.PostMessageAsync(new ScheduleCompactionMessage { ... });

// Wait for background work to complete
await runtime.WaitForQuiesceAsync();

// Inspect what happened
var stats = runtime.GetStats();
var log = runtime.GetIntentLog();
```

## Testing Strategy (Next Steps)

1. **Unit tests** - Test individual tasks and managers
2. **Integration tests** - Test actor runtime with real messages/tasks
3. **Determinism tests** - Run same workload twice, verify intent logs match
4. **Failure tests** - Simulate task failures, verify retry behavior
5. **Cloud tests** - Mock cloud storage, test upload/download logic

## Benefits

| Aspect | Benefit |
|--------|---------|
| **Embeddability** | In-process library, no RPC layer needed |
| **Determinism** | Same input ? same execution sequence ? reproducible results |
| **Predictability** | All background work sequenced through actor, no random threads |
| **Cloud-native** | WAL and SST naturally fit cloud storage patterns |
| **Debuggability** | Intent log shows every decision made |
| **Testability** | Can record/replay workloads for regression testing |
| **Extensibility** | Pluggable cloud backends, custom task types |

## Future Enhancements

1. **Adaptive compaction** - Actor adjusts strategy based on workload patterns
2. **Multi-tenancy** - Separate actor per tenant database
3. **Replication** - Actor broadcasts state changes to replicas
4. **Distributed consensus** - Use actor for leader election
5. **Workload analysis** - Export intent log for analytics
6. **Cloud provider SDKs** - Implement real cloud storage backends
7. **Manifest compaction** - Prune old compaction history

## Build Status

? **Build successful** - All code compiles cleanly with no warnings or errors.

The implementation is complete and ready for:
- Integration with existing `DbEngine`
- Implementation of real cloud storage backends (S3, Azure, etc.)
- Adding comprehensive unit and integration tests
- Performance benchmarking
