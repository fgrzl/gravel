# Refactoring Plan: Legacy Code Removal & Actor-Based LSM

## Phase 1: Identify All Legacy Code to Remove

### FileSystem-based Storage (to be replaced)
- `src/Gravel/Storage/FileSystem/Sst/*` - RocksDB-like SST (FileSstWriter, FileSstReader, FileSstFactory)
- `src/Gravel/Storage/FileSystem/Wal/*` - Custom WAL format (FileWalWriter, FileWalReader, FileWalFactory)
- `src/Gravel/Storage/Shared/*` - Shared block builders, filters, etc.

### Abstractions to Replace
- `src/Gravel/Abstractions/Storage/Sst/*` - ISstFactory, ISstWriter, ISstReader
- `src/Gravel/Abstractions/Storage/Wal/*` - IWalFactory, IWalWriter, IWalReader, WalConstants, WalRecord

### Integration Points to Refactor
- `src/Gravel/Engine/Managers/WalManager.cs` - Old WAL abstraction
- `src/Gravel/Engine/Managers/SstManager.cs` - Old SST abstraction
- `src/Gravel/Engine/DbEngine.cs` - Depends on old abstractions

## Phase 2: What We Keep

### Core TLV Infrastructure ? (DONE)
- `src/Gravel/Storage/TLV/TLVFormat.cs`
- `src/Gravel/Storage/TLV/TLVReader.cs`
- `src/Gravel/Storage/TLV/TLVWriter.cs`

### Storage Modes ? (DONE)
- `src/Gravel/Storage/Local/LocalWAL.cs`
- `src/Gravel/Storage/Local/LocalSSTManager.cs`
- `src/Gravel/Storage/HybridCloud/HybridCloudWAL.cs`
- `src/Gravel/Storage/HybridCloud/HybridCloudSSTManager.cs`
- `src/Gravel/Storage/StorageFactory.cs`

### Actor Infrastructure ? (DONE)
- `src/Gravel/Actor/*` - All actor-related code
- `src/Gravel/Cloud/Integration/ActorAwareWALManager.cs`

### Engine Core (to refactor, not remove)
- `src/Gravel/Engine/MemTable.cs` - Keep, but simplify
- `src/Gravel/Engine/Levels.cs` - Keep, refactor to use new storage
- `src/Gravel/Engine/DbEngine.cs` - Refactor to use new storage layer
- `src/Gravel/Abstractions/DbEntry.cs` - Keep
- `src/Gravel/Abstractions/IDbEngine.cs` - Keep

## Phase 3: New Architecture

```
User API (IDbEngine)
    ?
DbEngine (refactored)
    ?? Write Path: Transactions ? memtable ? WAL
    ?? Read Path: memtable ? Levels ? Cloud SSTs
    ?? Background: Actor-driven compaction & flush
    ?
ActorRuntime (task dispatch, ordering)
    ?? UploadWalSegment
    ?? FlushMemTable
    ?? CompactLevel
    ?
StorageLayer (unified abstraction)
    ?? LocalWAL / HybridCloudWAL
    ?? LocalSSTManager / HybridCloudSSTManager
    ?? TLVFormat (zero-copy serialization)
    ?
Cloud Storage / Local Disk
```

## Phase 4: Migration Steps

1. **Remove old FileSystem storage** (FileWalWriter, FileWalReader, FileSstWriter, FileSstReader)
2. **Remove old abstractions** (IWalFactory, ISstFactory, etc.)
3. **Refactor WalManager** to use StorageInstance
4. **Refactor SstManager** to use StorageInstance
5. **Refactor DbEngine** to work without old abstractions
6. **Update tests** to use new storage
7. **Verify build & functionality**

## Files to Delete

```
# FileSystem WAL
src/Gravel/Storage/FileSystem/Wal/FileWalWriter.cs
src/Gravel/Storage/FileSystem/Wal/FileWalReader.cs
src/Gravel/Storage/FileSystem/Wal/FileWalFactory.cs
src/Gravel/Storage/FileSystem/Wal/FileWalOptions.cs

# FileSystem SST
src/Gravel/Storage/FileSystem/Sst/FileSstWriter.cs
src/Gravel/Storage/FileSystem/Sst/FileSstReader.cs
src/Gravel/Storage/FileSystem/Sst/FileSstFactory.cs
src/Gravel/Storage/FileSystem/Sst/FileSstOptions.cs

# Shared infrastructure (RocksDB-like blocks)
src/Gravel/Storage/Shared/* (all files)

# Old abstractions
src/Gravel/Abstractions/Storage/Sst/ISstFactory.cs
src/Gravel/Abstractions/Storage/Sst/ISstWriter.cs
src/Gravel/Abstractions/Storage/Sst/ISstReader.cs
src/Gravel/Abstractions/Storage/Sst/SstOptions.cs
src/Gravel/Abstractions/Storage/Sst/CompressionKind.cs
src/Gravel/Abstractions/Storage/Sst/IBlockCompressor.cs

src/Gravel/Abstractions/Storage/Wal/IWalFactory.cs
src/Gravel/Abstractions/Storage/Wal/IWalWriter.cs
src/Gravel/Abstractions/Storage/Wal/IWalReader.cs
src/Gravel/Abstractions/Storage/Wal/WalOptions.cs
src/Gravel/Abstractions/Storage/Wal/WalConstants.cs
src/Gravel/Abstractions/Storage/Wal/WalRecord.cs

# In-memory implementations (replace with actor-based equivalents)
src/Gravel/Storage/InMemory/Wal/InMemoryWalWriter.cs
src/Gravel/Storage/InMemory/Wal/InMemoryWalReader.cs
src/Gravel/Storage/InMemory/Wal/InMemoryWalFactory.cs
src/Gravel/Storage/InMemory/Wal/InMemoryWalOptions.cs

src/Gravel/Storage/InMemory/Sst/InMemorySst.cs
src/Gravel/Storage/InMemory/Sst/InMemorySstWriter.cs
src/Gravel/Storage/InMemory/Sst/InMemorySstFactory.cs
src/Gravel/Storage/InMemory/Sst/InMemorySstOptions.cs

# Compression (simplify or remove)
src/Gravel/Compression/* (may keep simplified version)
```

## Refactor Priority

1. **High Priority** (blocks other work)
   - Remove old abstractions
   - Refactor WalManager
   - Refactor SstManager
   - Refactor DbEngine

2. **Medium Priority** (cleanup)
   - Delete FileSystem storage implementations
   - Delete In-Memory storage implementations
   - Delete Shared block builders

3. **Low Priority** (optimization)
   - Compression (simplify or defer)
   - Test refactoring

## Benefits of This Approach

? Cleaner codebase (no RocksDB legacy)
? Actor-driven operations (deterministic, testable)
? Zero-copy TLV format (efficient)
? Cloud-first design (hybrid mode ready)
? Unified storage interface (local/hybrid)
? Smaller attack surface
