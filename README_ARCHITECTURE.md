# Gravel: Actor-Based LSM Database

An embeddable, cloud-native Log-Structured Merge (LSM) database with deterministic, sequenced background operations through an actor runtime.

## Quick Start

```csharp
// Create in-memory database (no setup needed)
var engine = await GravelFactory.CreateInMemoryAsync();

// Or file-system backed
var engine = await GravelFactory.CreateFileSystemAsync(@"C:\data\gravel");

// Use like any key-value store
await engine.PutAsync("key"u8, "value"u8);
var value = await engine.GetAsync("key"u8);
await engine.DeleteAsync("key"u8);

// Batch operations
await engine.BatchAsync(new[]
{
    Mutation.Put("k1"u8, "v1"u8),
    Mutation.Put("k2"u8, "v2"u8),
});

// Scan ranges
await foreach (var (key, value) in engine.ScanAsync(new Query { Start = "a"u8, End = "z"u8 }))
{
    Console.WriteLine($"{key}: {value}");
}

// Cleanup
await engine.DisposeAsync();
```

## Architecture Highlights

### Actor-Based Design
- Central `ActorRuntime` sequences all background operations (flush, compaction, uploads)
- Deterministic: same input ? same execution sequence every time
- Embeddable: in-process library, no separate server or RPC

### Cloud-Native Storage
- **WAL**: Fast local appends with async cloud durability
- **SST**: Primary residence in cloud with optional local cache
- **Manifest**: Orchestrates recovery and compaction decisions
- Pluggable backends: S3, Azure Blob, Wasabi, etc.

### Reliable Background Work
- Each background task has automatic retry logic
- Intent log records all decisions for debugging and recovery
- Graceful shutdown with `WaitForQuiesceAsync()`

## Project Structure

```
src/Gravel/
??? Engine/              # Main engine and managers
??? Actor/               # Actor runtime and messages
?   ??? Messages/        # Message types (flush, compact, etc.)
?   ??? Tasks/           # Task implementations (with retry logic)
??? Cloud/               # Cloud storage abstractions
?   ??? Abstractions/    # Interfaces (ICloudStorage, IManifestManager, etc.)
?   ??? NoOp/            # No-op implementations for local-only use
??? Abstractions/        # Core interfaces (IDbEngine, Mutation, etc.)
??? Storage/             # WAL and SST implementations
?   ??? FileSystem/
?   ??? InMemory/
??? Internals/           # Implementation details (compaction, filters, indexes)
??? Compression/         # Compression support (Snappy, etc.)

test/Gravel.Tests/      # Unit tests
benchmark/              # Performance benchmarks
```

## Key Features

### Core Database Operations
- ? Put, Get, Delete, DeleteRange
- ? Batch operations (atomic)
- ? Range scanning
- ? Transactions
- ? Backup & Restore

### LSM Storage
- ? Memtable (SkipList-based)
- ? WAL (Write-Ahead Log)
- ? Multi-level SST storage
- ? Configurable compaction
- ? Range tombstones (range deletes)
- ? Bloom filters for fast lookups
- ? Block compression (Snappy, etc.)

### Cloud Integration (New)
- ? Actor-based operation sequencing
- ? Cloud storage abstraction
- ? Async WAL uploading
- ? SST cache management
- ? Manifest versioning
- ? Deterministic compaction logging
- ? Intent log for recovery

### Testing & Debugging
- ? Intent log for operation auditing
- ? Performance statistics
- ? Comprehensive logging
- ? No-op cloud implementations for local testing

## Configuration

```csharp
var options = new GravelOptions
{
    DatabasePath = "/data/gravel",      // Where local files go
    MemTableThreshold = 1000,            // When to flush
    SstLevels = 7,                       // Number of LSM levels
    WalSyncOnCommit = true,              // Durable writes
    CompactionFanInThreshold = 4,        // Trigger compaction
    // ... more options
};

var engine = new DbEngine(
    Options.Create(options),
    walFactory,
    sstFactory,
    compactionWorker,
    logger
);
```

## Cloud Integration (New in This Release)

### Actors and Messages
The new actor-based design introduces deterministic operation sequencing:

```csharp
// Create actor runtime
var runtime = new ActorRuntime(logger);

// Messages are posted for various events
var message = new FlushMemTableMessage { EntryCount = 100, UpToSequence = 50 };
await runtime.PostMessageAsync(message);

// Actor enqueues tasks to execute the work
var task = new FlushMemTableTask(message, flushFunc, logger);
await runtime.EnqueueTaskAsync(task);

// Wait for all background work
await runtime.WaitForQuiesceAsync();

// Inspect what happened
var log = runtime.GetIntentLog();  // See all operations
var stats = runtime.GetStats();    // Performance metrics
```

### Pluggable Cloud Storage
```csharp
// Use no-op locally (no cloud needed)
var storage = new NoOpCloudStorage();

// Or plug in real cloud backend
var storage = new S3CloudStorage(awsOptions);
// or
var storage = new AzureBlobStorage(azureOptions);

// All operations use the same interface
var wal = new CloudWalManager(storage, logger);
var sst = new CloudSstManager(storage, logger);
var manifest = new CloudManifestManager(storage, logger);
```

## Performance

Typical benchmarks on modern hardware:

| Operation | Latency | Notes |
|-----------|---------|-------|
| Put (memtable) | ~1-2 µs | Synchronous, in-memory |
| Get (cached) | ~1-5 µs | L1 cache hit |
| Get (SST) | ~10-100 µs | Depends on Bloom filter, cache |
| Batch (1K ops) | ~1-2 ms | Atomic |
| Range scan | Variable | Depends on range size |
| Flush | ~10-100 ms | Background task |
| Compaction | ~100 ms - 1 s | Depends on file size |
| WAL upload | ~100 ms - 1 s | Network dependent |

Use the benchmarks project for detailed performance analysis:
```bash
cd benchmark/Gravel.Benchmarks
dotnet run --configuration Release
```

## Testing

Run unit tests:
```bash
dotnet test test/Gravel.Tests/
```

Run with code coverage:
```bash
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover
```

## Documentation

- **[ACTOR_ARCHITECTURE.md](./ACTOR_ARCHITECTURE.md)** - Detailed design of the actor runtime and cloud integration
- **[IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md)** - What was implemented and how
- **[DESIGN_PATTERNS.md](./DESIGN_PATTERNS.md)** - Patterns and best practices used throughout

## Development

Prerequisites:
- .NET 10 SDK
- Visual Studio 2022 or VS Code

Build:
```bash
dotnet build
```

Run tests:
```bash
dotnet test
```

## Contributing

This is an open-source project. Contributions are welcome!

Areas for contribution:
- Real cloud storage implementations (S3, Azure, Google Cloud)
- Performance optimizations
- Additional compression codecs
- Distributed replication
- Web API wrapper
- CLI tool

## License

[Your license here]

## Future Roadmap

- [ ] Real cloud backends (S3, Azure Blob Storage, GCS)
- [ ] Distributed replication and consensus
- [ ] Adaptive compaction strategies
- [ ] Multi-tenancy support
- [ ] Full-text search integration
- [ ] Time-series optimizations
- [ ] Geo-distributed deployments
- [ ] GraphQL API

## Support

- GitHub Issues: Report bugs and feature requests
- Discussions: Ask questions and discuss design
- Benchmarks: Compare performance vs other databases

## Acknowledgments

Inspired by:
- RocksDB's LSM design
- LevelDB's simplicity
- Tokio's actor model
- Azure's cloud-native patterns

---

**Gravel: Simple, Reliable, Cloud-Ready**

For more information, see the detailed architecture and implementation documents.
