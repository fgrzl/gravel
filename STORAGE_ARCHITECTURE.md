# Storage Architecture Strategy

## Overview
Two distinct storage modes, each optimized for its deployment scenario:

### 1. **Local-Only Mode** (`Storage.Local`)
- **Use Case**: Single-machine deployments, development, testing
- **WAL**: Memory buffer ? disk segments (no cloud)
- **SST**: Disk-resident, full copies
- **Manifest**: Local file-based versioning
- **No cloud dependencies**

### 2. **Hybrid Cloud Mode** (`Storage.HybridCloud`)
- **Use Case**: Production with cloud durability
- **WAL**: Memory buffer ? ephemeral local disk ? cloud (source of truth)
- **SST**: Cloud object store (source) + optional local NVMe cache
- **Manifest**: Cloud-versioned (single source of truth)
- **Recovery**: Always from cloud + WAL segments
- **Local storage**: Ephemeral, can be lost without data loss

## Key Design Principles

### Unified Interface
Both modes implement the same abstractions:
- `IWALWriter` / `IWALReader`
- `ICloudSstManager` (works with no-op for local mode)
- `IManifestManager` (works with local for local mode)

### Actor Integration
Both modes post messages to `ActorRuntime` for:
- Flush triggers (when memtable exceeds threshold)
- Compaction scheduling (when levels exceed thresholds)
- WAL segment upload (hybrid cloud only)
- Manifest sync (periodic, after significant changes)
- SST eviction (hybrid cloud only, when cache full)

### Data Durability Guarantees

**Local-Only Mode:**
- Single replica on disk
- WAL provides durability via sync-on-commit option
- Backup/restore via tar.gz archives

**Hybrid Cloud Mode:**
- Cloud is authoritative for SST and manifest
- Local cache is transparent, ephemeral
- Recovery always possible from cloud
- WAL segments uploaded asynchronously but buffered locally until confirmed

## File Organization

```
src/Gravel/Storage/
??? TLV/                           # Shared zero-copy format
?   ??? TLVFormat.cs
?   ??? TLVReader.cs
?   ??? TLVWriter.cs
??? Local/                         # Local-only implementations
?   ??? LocalWAL.cs
?   ??? LocalSSTWriter.cs
?   ??? LocalSSTReader.cs
??? HybridCloud/                   # Hybrid cloud implementations
?   ??? HybridCloudWAL.cs
?   ??? HybridCloudSSTManager.cs
?   ??? HybridCloudManifest.cs
??? Integration/
    ??? StorageFactory.cs          # Creates correct mode based on config
    ??? ActorStorageAdapter.cs     # Bridges actor runtime to storage
```

## Configuration

```csharp
// Local-only
var config = new StorageConfig
{
    Mode = StorageMode.LocalOnly,
    LocalPath = "/data/gravel"
};

// Hybrid cloud
var config = new StorageConfig
{
    Mode = StorageMode.HybridCloud,
    LocalCachePath = "/data/gravel-cache",  // Ephemeral
    CloudStorage = s3Client,
    CloudBucket = "my-db-bucket",
    LocalCacheSizeBytes = 100 * 1024 * 1024, // 100 MB
    MaxWalBufferBytes = 10 * 1024 * 1024     // 10 MB per segment
};

var storage = StorageFactory.Create(config, actorRuntime);
```

## Migration Path

1. **Start local-only** for development/testing
2. **Switch to hybrid cloud** for production (transparent API change)
3. **Leverage cloud durability** while maintaining performance via caching
4. **Zero application code changes** - configuration only

## Determinism & Recovery

**Local-Only:**
- Recovery: Replay WAL from disk in order
- Determinism: Same WAL replay = same state

**Hybrid Cloud:**
- Recovery: Load manifest from cloud ? restore SSTs from cloud ? replay WAL segments
- Determinism: Cloud manifest is immutable, WAL segments ordered by sequence

Both modes support exact replay via intent log + manifest + WAL.
