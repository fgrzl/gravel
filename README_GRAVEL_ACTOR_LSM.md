# Gravel: Actor-Based LSM with Zero-Copy TLV Storage

## ?? Vision

Build an **actor-driven, cloud-first LSM database** with:
- **Zero-copy TLV serialization** for efficiency
- **Hybrid cloud architecture** (cloud = truth, local = ephemeral cache)
- **Actor-based background tasks** (compaction, WAL uploads)
- **Clean codebase** (no RocksDB legacy baggage)
- **Tiered testing & benchmarking** (fast feedback loops)

## ?? Architecture Overview

```
???????????????????????????????????????
?         Application Layer           ?
?       (IDbEngine interface)         ?
???????????????????????????????????????
               ?
???????????????????????????????????????
?         DbEngine                    ?
?  (Refactored: uses StorageInstance) ?
???????????????????????????????????????
               ?
???????????????????????????????????????
?     StorageFactory.Create()         ?
?   (Config-driven mode selection)    ?
???????????????????????????????????????
               ?
      ???????????????????
      ?                 ?
????????????????  ?????????????????
?Local-Only    ?  ?Hybrid Cloud   ?
?              ?  ?               ?
?LocalWAL      ?  ?HybridCloudWAL ???? Actor Runtime
?LocalSST      ?  ?HybridCloudSST ?    (Upload messages)
?              ?  ?   + Cache     ?
????????????????  ?????????????????
      ?                ?
      ??????????????????
               ?
        ?????????????????
        ? TLVFormat     ?
        ? (9-byte wire) ?
        ?????????????????
               ?
        ?????????????????
        ?Cloud Storage /?
        ?Local Disk     ?
        ?????????????????
```

## ??? Core Components

### 1. Zero-Copy TLV Format
**Location:** `src/Gravel/Storage/TLV/`

9-byte wire format:
```
[Type:1] [KeyLen:4] [ValLen:4] [Key:variable] [Value:variable]
```

Benefits:
- No intermediate allocations during reads (slices into buffer)
- Streaming write support (async)
- Cloud-native (compact, efficient)

### 2. Storage Modes

#### Local-Only Mode
```
MemTable ? LocalWAL (disk segments) + LocalSST (disk files)
```
- Perfect for: Development, testing, single-machine
- Recovery: Replay WAL from disk

#### Hybrid Cloud Mode
```
MemTable ? HybridCloudWAL (memory ? disk ? cloud) + HybridCloudSST (cloud + NVMe cache)
```
- Perfect for: Production, multi-tenant, cloud-native
- Recovery: Cloud is source of truth, WAL for ordering
- Cache: Ephemeral, LRU eviction, loss is safe

### 3. Actor Runtime Integration
**Location:** `src/Gravel/Actor/`

Messages:
- `UploadWalSegmentMessage` - WAL segment to cloud
- `FlushMemTableMessage` - MemTable to SST
- `CompactLevelMessage` - Level compaction

Benefits:
- Deterministic ordering (preserves sequence)
- Background operations (non-blocking)
- Retry logic (automatic)
- Rate limiting (fair)

### 4. Cloud-Native SST
**Location:** `src/Gravel/Cloud/SST/`

Structure:
```
[Data Block (TLV)] [Index Block] [Metadata] [Footer]
```
- Data: TLV-encoded entries
- Index: Sparse (every Nth key)
- Metadata: Entry count, timestamps
- Footer: Offsets to blocks

## ?? File Organization

```
src/Gravel/
??? Actor/                      ? Actor runtime (existing)
??? Cloud/
?   ??? Abstractions/          ? ICloudStorage
?   ??? SST/                   ? CloudNativeSSTWriter
?   ??? WAL/                   ? CloudNativeWAL
?   ??? Integration/           ? ActorAwareWALManager
??? Storage/
?   ??? TLV/                   ? TLVFormat, TLVReader, TLVWriter
?   ??? Local/                 ? LocalWAL, LocalSSTManager
?   ??? HybridCloud/           ? HybridCloudWAL, HybridCloudSSTManager
?   ??? StorageFactory.cs      ? Unified factory
??? Abstractions/              ? DbEntry, IDbEngine (reused)

test/Gravel.Tests/             ? Single project, 6 tier folders
??? Tier1Hotpath/
?   ??? TLV/
??? Tier2Subsystem/
?   ??? Local/
??? Tier3System/
??? Tier4Integration/
??? Tier5Soak/
??? Tier6Capacity/

benchmark/Gravel.Benchmarks/   ? Single executable, 6 tier folders
??? Tier1Hotpath/
??? Tier2Subsystem/
??? Tier3System/
??? Tier4Integration/
??? Tier5Soak/
??? Tier6Capacity/
```

## ?? Testing Strategy

### Tier 1: Hot Path (~100ms)
Validates critical 1-3 operations:
- TLV read/write
- MemTable insert/get
- WAL append

### Tier 2: Subsystem (~1s)
Validates components in isolation:
- LocalWAL operations
- HybridCloudWAL with actor
- LocalSSTManager I/O
- Actor dispatch latency

### Tier 3: System (~10s)
Validates subsystems working together:
- Full write path
- Full read path
- Compaction actor
- Recovery from WAL/cloud

### Tier 4: Integration (~60s)
Validates end-to-end scenarios:
- Multi-node recovery
- Cloud durability
- Cache behavior
- Failure modes

### Tier 5: Soak (~10min)
Finds memory leaks and degradation:
- 1M entry throughput
- Background compaction 24h
- Crash/recovery cycles
- Cache churn

### Tier 6: Capacity (~1h)
Benchmarks limits and scalability:
- 1GB+ dataset
- Compaction throughput
- Cache efficiency
- Multi-tenant isolation

## ?? Benchmark Strategy

All benchmarks in `benchmark/Gravel.Benchmarks/` using BenchmarkDotNet:

```csharp
[SimpleJob(warmupCount: 3, targetCount: 5)]
[MemoryDiagnoser]
public class TLVBenchmarks
{
    [Benchmark]
    public void Operation() { }
}
```

Organized by tier:
- Tier 1: 5-10 simple microbenchmarks
- Tier 2: 10-15 subsystem benchmarks
- Tier 3: 5-10 system benchmarks
- Tier 4: 3-5 integration benchmarks
- Tier 5: 2-3 soak benchmarks
- Tier 6: 1-2 capacity benchmarks

## ?? Data Flow Examples

### Write Path (Hybrid Cloud)
```
Client.PutAsync(key, value)
    ? Transaction
    ? WAL.AppendAsync() [Memory, <1ms]
    ? Buffer full? ? Queue UploadWalSegmentMessage
    ? MemTable.Put()
    ? Return to client
    
(Background) Actor: Download segment ? Cloud [retry]
```

### Read Path (Hybrid Cloud)
```
Client.GetAsync(key)
    ? Check MemTable [cache hit likely]
    ? Check Levels
    ? Load SST from Cloud [or cache]
    ? Return value
```

### Recovery (Hybrid Cloud)
```
Database restart
    ? Load manifest from Cloud
    ? Restore SSTs to cache
    ? Load WAL segments from Cloud
    ? Replay WAL in order
    ? MemTable reconstructed
    ? Ready for queries
```

## ?? Success Metrics

? **Build**: Compiles without errors or warnings
? **Tests**: All 6 tiers pass with correct timing
? **Performance**: TLV reads/writes <1µs per entry
? **Cloud Efficiency**: Async uploads don't block writes
? **Cache**: LRU eviction works, loss doesn't affect correctness
? **Recovery**: Cloud-based recovery is deterministic
? **Actor**: Messages preserve WAL ordering

## ?? Getting Started

```bash
# Build
dotnet build

# Test hot path (fast feedback)
dotnet test test/Gravel.Tests.Tier1Hotpath/

# Test everything
dotnet test test/Gravel.Tests*/

# Benchmark
dotnet run -c Release -p benchmark/Gravel.Benchmarks/
```

## ?? Documentation

- `STORAGE_ARCHITECTURE.md` - Design details & migration path
- `EXECUTION_ROADMAP.md` - Step-by-step completion plan
- `REFACTORING_STATUS.md` - Current progress
- `test/Gravel.Tests.TierArchitecture/README.md` - Test organization
- `benchmark/Gravel.Benchmarks/BENCHMARK_ARCHITECTURE.md` - Benchmark organization
- `ARCHITECTURE_DIAGRAMS.md` - Visual flows

## ?? Key Design Decisions

1. **Zero-Copy TLV** - Efficient serialization, minimal allocations
2. **Cloud = Truth** - Hybrid cloud always recovers from cloud
3. **Local Cache Ephemeral** - Loss of cache never causes data loss
4. **Actor-Driven** - Deterministic, ordered background operations
5. **Unified API** - Same code for local and hybrid modes
6. **Tiered Testing** - Fast feedback (Tier 1) to comprehensive (Tier 6)

## ?? Future Enhancements

- [ ] Manifest management (cloud-versioned)
- [ ] Distributed compaction (across nodes)
- [ ] Incremental backups (delta from cloud)
- [ ] Cloud provider adapters (S3, Azure, GCS)
- [ ] Advanced caching (working set estimation)
- [ ] Multi-tenant isolation guarantees
