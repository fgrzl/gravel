# Legacy Code Status: What's Deleted vs What Remains

## ??? Deleted (Complete Removal)

### FileSystem WAL Implementation
```
src/Gravel/Storage/FileSystem/Wal/
??? FileWalWriter.cs          ? DELETED
??? FileWalReader.cs          ? DELETED
??? FileWalFactory.cs         ? DELETED
??? FileWalOptions.cs         ? DELETED
```
**Replaced by**: LocalWAL (disk segments with auto-roll)

### FileSystem SST Implementation
```
src/Gravel/Storage/FileSystem/Sst/
??? FileSstWriter.cs          ? DELETED
??? FileSstReader.cs          ? DELETED
??? FileSstFactory.cs         ? DELETED
??? FileSstOptions.cs         ? DELETED
```
**Replaced by**: LocalSSTManager + CloudNativeSSTWriter

### SST Abstractions
```
src/Gravel/Abstractions/Storage/Sst/
??? ISstFactory.cs            ? DELETED
??? ISstWriter.cs             ? DELETED
??? ISstReader.cs             ? DELETED
??? SstOptions.cs             ? DELETED
??? IBlockCompressor.cs       ? DELETED
```
**Replaced by**: TLVFormat (simple, direct serialization)

---

## ? To Be Deleted (Next Phase)

### WAL Abstractions
```
src/Gravel/Abstractions/Storage/Wal/
??? IWalFactory.cs            ? TO DELETE
??? IWalWriter.cs             ? TO DELETE
??? IWalReader.cs             ? TO DELETE
??? WalConstants.cs           ? TO DELETE
??? WalRecord.cs              ? TO DELETE
??? WalOptions.cs             ? TO DELETE
```
**Replacement action**: Remove dependency chain
- Delete abstractions
- Update WalManager to use StorageInstance.LocalWAL/HybridWAL directly
- Update recovery code to use TLVReader instead of WalRecord

### In-Memory Implementations
```
src/Gravel/Storage/InMemory/
??? Wal/
?   ??? InMemoryWalWriter.cs  ? TO DELETE
?   ??? InMemoryWalReader.cs  ? TO DELETE
?   ??? InMemoryWalFactory.cs ? TO DELETE
?   ??? InMemoryWalOptions.cs ? TO DELETE
??? Sst/
    ??? InMemorySst.cs        ? TO DELETE
    ??? InMemorySstWriter.cs  ? TO DELETE
    ??? InMemorySstFactory.cs ? TO DELETE
    ??? InMemorySstOptions.cs ? TO DELETE
```
**Replacement action**: In-memory testing can use disk with temp directories

### RocksDB-Like Block Infrastructure
```
src/Gravel/Storage/Shared/
??? BlockHandle.cs            ? TO DELETE
??? DataBlockBuilder.cs       ? TO DELETE
??? SimpleBlockBuilder.cs     ? TO DELETE
??? FullFilterBlockBuilder.cs ? TO DELETE
??? RangeDeleteBlockBuilder.cs ? TO DELETE
??? (other block builders)    ? TO DELETE
```
**Replacement action**: All data now goes through TLVFormat

### Compression Infrastructure
```
src/Gravel/Compression/
??? Default/
?   ??? ZeroCompressor.cs     ? REVIEW
??? ICompressorFactory.cs     ? REVIEW
??? (compression codecs)      ? SIMPLIFY
```
**Replacement action**: Keep minimal compression, remove RocksDB integration

---

## ? Kept (No Changes Needed - Yet)

### Core Abstractions
```
src/Gravel/Abstractions/
??? DbEntry.cs               ? KEEP (core data model)
??? IDbEngine.cs             ? KEEP (public API)
??? IAsyncInitializable.cs   ? KEEP (standard interface)
??? Mutation.cs              ? KEEP (transaction data)
```

### Actor Runtime
```
src/Gravel/Actor/
??? ActorRuntime.cs          ? KEEP (message dispatch)
??? ActorOptions.cs          ? KEEP (configuration)
??? ActorMessage.cs          ? KEEP (base class)
??? Messages/                ? KEEP (all message types)
??? Executors/               ? KEEP (message handlers)
??? (actor infrastructure)   ? KEEP
```

### New Storage Layer (Fully Integrated)
```
src/Gravel/Storage/
??? TLV/
?   ??? TLVFormat.cs         ? KEEP (9-byte wire format)
?   ??? TLVReader.cs         ? KEEP (zero-copy reader)
?   ??? TLVWriter.cs         ? KEEP (async writer)
??? Local/
?   ??? LocalWAL.cs          ? KEEP (local disk WAL)
?   ??? LocalSSTManager.cs   ? KEEP (local disk SSTs)
??? HybridCloud/
?   ??? HybridCloudWAL.cs    ? KEEP (memory + cloud)
?   ??? HybridCloudSSTManager.cs ? KEEP (cache + cloud)
??? StorageFactory.cs        ? KEEP (unified factory)
```

### Cloud Integration
```
src/Gravel/Cloud/
??? Abstractions/
?   ??? ICloudStorage.cs     ? KEEP (cloud provider interface)
??? SST/
?   ??? CloudNativeSSTWriter.cs ? KEEP (cloud SST format)
??? WAL/
?   ??? CloudNativeWAL.cs    ? KEEP (cloud WAL format)
??? Integration/
    ??? ActorAwareWALManager.cs ? KEEP (WAL ? actor bridge)
```

### Engine Core (To Be Refactored)
```
src/Gravel/Engine/
??? MemTable.cs              ? KEEP (refactor usage only)
??? Levels.cs                ? KEEP (refactor usage only)
??? DbEngine.cs              ? KEEP (refactor: remove old abstractions)
??? Managers/
?   ??? WalManager.cs        ? KEEP (refactor: use StorageInstance)
?   ??? SstManager.cs        ? KEEP (refactor: use StorageInstance)
??? (other components)       ? KEEP
```

### Utilities & Infrastructure
```
src/Gravel/Internals/
??? Buf.cs                   ? KEEP (buffer pooling)
??? ByteComparer.cs          ? KEEP (key comparison)
??? Crc32C.cs                ? KEEP (checksums)
??? Varint.cs                ? KEEP (variable-length encoding)
??? (utilities)              ? KEEP

src/Gravel/Logging/
??? Log.cs                   ? KEEP (logging helpers)
??? (logging)                ? KEEP

src/Gravel/Telemetry/
??? TelemetrySources.cs      ? KEEP (observability)
??? (observability)          ? KEEP
```

---

## ?? Deletion Impact Analysis

### High Impact (Many References)
- `IWalFactory` - Referenced in WalManager, DbEngine, DI setup
  - **Impact**: 3-5 locations
  - **Fix**: Replace with StorageInstance.LocalWAL/HybridWAL
  
- `IWalWriter` - Used for writes everywhere
  - **Impact**: Throughout WalManager
  - **Fix**: Replace with LocalWAL.AppendAsync or HybridCloudWAL.AppendAsync
  
- `IWalReader` - Used for recovery
  - **Impact**: Recovery code, WalManager
  - **Fix**: Replace with TLVReader.EnumerateAll()

### Medium Impact (Some References)
- `InMemory*` - Test infrastructure
  - **Impact**: Test fixtures
  - **Fix**: Use LocalWAL with temp directories instead
  
- `Shared/*` - Block builders
  - **Impact**: Old FileSstWriter (already deleted)
  - **Fix**: None needed, already gone

### Low Impact (Few References)
- `WalConstants`, `WalRecord`, `WalOptions` - Configuration
  - **Impact**: DI setup, old abstractions
  - **Fix**: Remove usage, simplify configuration

---

## ?? Deletion Order (Recommended)

### Step 1: Remove Abstractions (Breaks build immediately)
```
src/Gravel/Abstractions/Storage/Wal/
??? IWalFactory.cs
??? IWalWriter.cs
??? IWalReader.cs
??? WalConstants.cs
??? WalRecord.cs
??? WalOptions.cs
```

### Step 2: Fix Core Engine (Rebuild passes)
```
src/Gravel/Engine/Managers/WalManager.cs    (refactor)
src/Gravel/Engine/Managers/SstManager.cs    (refactor)
src/Gravel/Engine/DbEngine.cs               (refactor)
```

### Step 3: Remove In-Memory (Breaks tests)
```
src/Gravel/Storage/InMemory/
```

### Step 4: Remove Shared Blocks (No references now)
```
src/Gravel/Storage/Shared/
```

### Step 5: Simplify Compression (Optional)
```
src/Gravel/Compression/
```

### Step 6: Migrate Tests (Add new, remove old)
```
test/Gravel.Tests/        (remove after migration)
test/Gravel.Tests.Tier*/  (keep, populate)
```

---

## ? Verification Checklist

After each deletion phase:

```bash
# Build check
dotnet build
# ? No errors
# ? No warnings
# ? All project references valid

# Test check
dotnet test test/Gravel.Tests/
# ? Tests compile
# ? Tests pass (or expected failures only)

# No orphaned references
grep -r "IWalFactory\|IWalWriter\|IWalReader" src/ benchmark/ test/
# ? Only expected matches (in comments, docs)
```

---

## ?? Deletion Timeline

| Phase | Files | Duration | Impact |
|-------|-------|----------|--------|
| Delete FileSystem WAL/SST | 8 | 5 min | ? No refs (already cleaned) |
| Delete Abstractions | 8 | 5 min | ? Breaks build |
| Fix Engine (refactor) | 3 | 30 min | ? Build passes |
| Delete InMemory | 8 | 5 min | ? Breaks tests |
| Fix Tests | - | 2 hr | ? Tests pass |
| Delete Shared | 6 | 5 min | ? No impact |
| Delete/Simplify Compression | 5 | 15 min | ? Keep if needed |
| **Total** | ~40 | **~3 hrs** | |

---

## ?? Success Criteria

? After deletion, these should be true:

1. **No references to deleted abstractions** in src/
2. **Build is clean** (no errors, no warnings)
3. **Tests migrate** to new structure
4. **Same functionality** with cleaner code
5. **StorageInstance** used consistently
6. **TLVFormat** handles all serialization
7. **Actor messages** flow correctly
