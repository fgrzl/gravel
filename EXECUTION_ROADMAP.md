# Execution Roadmap: Actor-Based LSM Completion

## Phase 1: Clean Legacy (IN PROGRESS)

### 1.1 Delete Old WAL Abstractions
```bash
rm src/Gravel/Abstractions/Storage/Wal/IWalFactory.cs
rm src/Gravel/Abstractions/Storage/Wal/IWalWriter.cs
rm src/Gravel/Abstractions/Storage/Wal/IWalReader.cs
rm src/Gravel/Abstractions/Storage/Wal/WalConstants.cs
rm src/Gravel/Abstractions/Storage/Wal/WalRecord.cs
rm src/Gravel/Abstractions/Storage/Wal/WalOptions.cs
```

### 1.2 Delete Old In-Memory Implementations
```bash
rm -r src/Gravel/Storage/InMemory/
```

### 1.3 Delete Shared Block Infrastructure
```bash
rm -r src/Gravel/Storage/Shared/
```

### 1.4 Simplify Compression
- Keep minimal interface if needed
- Remove RocksDB-specific compression codecs
- Consider removing entirely (use TLV's efficiency instead)

## Phase 2: Refactor Core Engine

### 2.1 Update WalManager
**Location:** `src/Gravel/Engine/Managers/WalManager.cs`

**Changes:**
```csharp
// OLD: Depends on IWalFactory, IWalWriter, IWalReader
public class WalManager
{
    readonly IWalFactory _walFactory;
    readonly IWalWriter _walWriter;
}

// NEW: Uses StorageInstance directly
public class WalManager
{
    readonly StorageInstance _storage;
    
    public async ValueTask AppendAsync(DbEntry entry, CancellationToken ct)
    {
        if (_storage.Mode == StorageMode.LocalOnly)
            await _storage.LocalWAL.AppendAsync(entry, ct);
        else
            await _storage.HybridWAL.AppendAsync(entry, ct);
    }
}
```

### 2.2 Update SstManager
**Location:** `src/Gravel/Engine/Managers/SstManager.cs`

**Changes:**
```csharp
// OLD: Depends on ISstFactory, ISstWriter, ISstReader
public class SstManager
{
    readonly ISstFactory _sstFactory;
}

// NEW: Uses StorageInstance directly
public class SstManager
{
    readonly StorageInstance _storage;
    
    public async ValueTask<string> WriteAsync(...)
    {
        if (_storage.Mode == StorageMode.LocalOnly)
            return await _storage.LocalSST.WriteAsync(...);
        else
            return await _storage.HybridSST.WriteAsync(...);
    }
}
```

### 2.3 Update DbEngine
**Location:** `src/Gravel/Engine/DbEngine.cs`

**Changes:**
- Replace `IWalFactory` parameter with `StorageInstance`
- Replace `ISstFactory` parameter with `StorageInstance`
- Update all WAL/SST calls to use StorageInstance methods

## Phase 3: Verify Compilation

### 3.1 Build
```bash
dotnet build
```

### 3.2 Expected Errors
- References to deleted abstractions in old test files
- References in benchmark files
- References in configuration/DI setup

### 3.3 Fix Remaining Issues
- Update test fixtures to use StorageFactory
- Update benchmark setup to use StorageFactory
- Update DI container registration (if using one)

## Phase 4: Test Migration

### 4.1 Analyze Old Tests
```bash
# See what tests reference old abstractions
grep -r "IWalFactory\|ISstFactory\|IWalWriter" test/Gravel.Tests/
```

### 4.2 Migrate to New Tiers
- **Tier 1**: Fast, focused on hot path
- **Tier 2**: Subsystem isolation tests
- **Tier 3**: Integration tests
- **Tier 4**: End-to-end scenarios

### 4.3 Remove Old Test Project
```bash
rm -r test/Gravel.Tests/  # After migrating tests
```

## Phase 5: Benchmark Implementation

### 5.1 Implement Tier Benchmarks
- **Tier 1**: TLV read/write, MemTable ops
- **Tier 2**: LocalWAL, HybridCloudWAL, LocalSST
- **Tier 3**: Write path, read path, compaction
- **Tier 4**: YCSB, concurrent clients
- **Tier 5**: Sustained throughput, memory stability
- **Tier 6**: Max QPS, large datasets

### 5.2 Add Benchmark Configuration
```csharp
// All benchmarks inherit from base config
[SimpleJob(warmupCount: 3, targetCount: 5)]
[MemoryDiagnoser]
public abstract class BenchmarkBase { }
```

## Phase 6: Validation

### 6.1 Compile Check
```bash
dotnet build
```

### 6.2 Test Tier 1
```bash
dotnet test test/Gravel.Tests.Tier1Hotpath/
```

### 6.3 Test Tier 1+2
```bash
dotnet test test/Gravel.Tests.Tier1Hotpath/ test/Gravel.Tests.Tier2Subsystem/
```

### 6.4 Run Benchmarks
```bash
dotnet run -c Release -p benchmark/Gravel.Benchmarks/
```

## Estimated Timeline

| Phase | Task | Duration |
|-------|------|----------|
| 1.1 | Delete WAL abstractions | 5 min |
| 1.2 | Delete InMemory | 5 min |
| 1.3 | Delete Shared | 5 min |
| 1.4 | Simplify Compression | 15 min |
| 2.1 | Refactor WalManager | 30 min |
| 2.2 | Refactor SstManager | 30 min |
| 2.3 | Refactor DbEngine | 30 min |
| 3.1-3.3 | Build & Fix | 30 min |
| 4.1-4.3 | Test Migration | 2 hours |
| 5.1-5.2 | Benchmark Implementation | 2 hours |
| 6.1-6.4 | Validation | 1 hour |
| **Total** | | **~6.5 hours** |

## Success Criteria

? Builds without errors
? Tier 1 tests pass (<100ms)
? Tier 2 tests pass (<1s)
? Benchmarks run without errors
? No references to old abstractions
? StorageInstance used consistently
? Actor messages flow correctly
? Local and Hybrid cloud modes work

## Rollback Points

- After Phase 1: Git checkpoint before deleting legacy code
- After Phase 2: Git checkpoint before test migration
- After Phase 3: Git checkpoint before test cleanup
