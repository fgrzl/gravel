# ?? Gravel Refactoring: Quick Reference

## What Just Happened

? **Implemented**: Zero-copy TLV storage, actor-driven WAL uploads, hybrid cloud mode
? **Created**: 6-tier test structure (Tier 1 hot path to Tier 6 capacity)
? **Created**: Single-executable benchmark suite with tier organization
? **Deleted**: RocksDB-like FileSystem WAL/SST and old abstractions

## Current Architecture

```
Application
    ?
DbEngine (needs refactor)
    ?
StorageInstance (from StorageFactory) ? NEW
?? LocalWAL (disk)
?? LocalSST (disk)
?? HybridCloudWAL (memory ? cloud)
?? HybridCloudSST (cache + cloud)
    ?
TLVFormat (zero-copy) ? NEW
    ?
Disk / Cloud + Cache
    ?
ActorRuntime (background uploads) ? NEW
```

## Key Files to Know

### New Storage Layer
| File | Purpose |
|------|---------|
| `src/Gravel/Storage/TLV/TLVFormat.cs` | 9-byte wire format |
| `src/Gravel/Storage/Local/LocalWAL.cs` | Disk WAL |
| `src/Gravel/Storage/HybridCloud/HybridCloudWAL.cs` | Memory + cloud WAL |
| `src/Gravel/Storage/StorageFactory.cs` | Unified factory |

### To Delete Next
| File | Status |
|------|--------|
| `src/Gravel/Abstractions/Storage/Wal/IWalFactory.cs` | ? Next phase |
| `src/Gravel/Abstractions/Storage/Wal/IWalWriter.cs` | ? Next phase |
| `src/Gravel/Storage/InMemory/*` | ? Next phase |
| `src/Gravel/Storage/Shared/*` | ? Next phase |

### Test Structure (Folders in test/Gravel.Tests/)
```
test/Gravel.Tests/
?? Tier1Hotpath/         ? Hot path (~100ms)
?  ?? TLV/
?? Tier2Subsystem/       ? Subsystem (~1s)
?  ?? Local/
?? Tier3System/          ? System (~10s)
?? Tier4Integration/     ? Integration (~60s)
?? Tier5Soak/            ? Soak (~10min)
?? Tier6Capacity/        ? Capacity (~1h)
```

### Benchmark Structure (Folders in benchmark/Gravel.Benchmarks/)
```
benchmark/Gravel.Benchmarks/  ? Single executable
?? Tier1Hotpath/
?? Tier2Subsystem/
?? Tier3System/
?? Tier4Integration/
?? Tier5Soak/
?? Tier6Capacity/
```

## Solution Structure (3 Projects Only)

```
Gravel.sln
?? src/Gravel/Gravel.csproj           ? Main library
?? test/Gravel.Tests/Gravel.Tests.csproj   ? All tests (6 tiers)
?? benchmark/Gravel.Benchmarks/Gravel.Benchmarks.csproj  ? All benchmarks (6 tiers)
```

## Next Steps (Recommended Order)

### 1. Delete Legacy Abstractions (30 min)
```bash
rm src/Gravel/Abstractions/Storage/Wal/IWal*.cs
rm src/Gravel/Abstractions/Storage/Wal/WalConstants.cs
rm src/Gravel/Abstractions/Storage/Wal/WalRecord.cs
rm src/Gravel/Abstractions/Storage/Wal/WalOptions.cs
```

### 2. Refactor Engine (1.5 hours)
- Update `src/Gravel/Engine/Managers/WalManager.cs`
- Update `src/Gravel/Engine/Managers/SstManager.cs`
- Update `src/Gravel/Engine/DbEngine.cs`

### 3. Build & Verify (30 min)
```bash
dotnet build  # Should be clean
```

### 4. Migrate Tests (2 hours)
- Move tests to new tier structure
- Remove old `test/Gravel.Tests/` directory

### 5. Implement Benchmarks (2 hours)
- Add benchmarks to each tier

### 6. Validate (1 hour)
```bash
dotnet test test/Gravel.Tests*/
dotnet run -c Release -p benchmark/Gravel.Benchmarks/
```

**Total: ~7 hours**

## Quick Commands

```bash
# Build
dotnet build

# Test specific tier
dotnet test test/Gravel.Tests.Tier1Hotpath/

# Test tiers 1-2 (fast)
dotnet test test/Gravel.Tests.Tier1Hotpath/ test/Gravel.Tests.Tier2Subsystem/

# All tests
dotnet test test/Gravel.Tests*/

# Benchmarks
dotnet run -c Release -p benchmark/Gravel.Benchmarks/
```

## Documentation Files

| Document | Purpose |
|----------|---------|
| `README_GRAVEL_ACTOR_LSM.md` | Complete architecture overview |
| `STORAGE_ARCHITECTURE.md` | Design & migration |
| `EXECUTION_ROADMAP.md` | Step-by-step plan |
| `DELETION_ROADMAP.md` | What to delete & when |
| `IMPLEMENTATION_CHECKLIST.md` | Detailed checklist |
| `CURRENT_STATE.md` | Accomplishments & next steps |

## Key Design Decisions

1. **TLV Format**: Simple 9-byte format, zero-copy reads
2. **Cloud = Truth**: Hybrid cloud always recovers from cloud
3. **Ephemeral Cache**: Loss of cache never causes data loss
4. **Actor-Driven**: Deterministic, background WAL uploads
5. **Unified API**: Same code for local and hybrid modes
6. **Tiered Testing**: Fast feedback (Tier 1) to comprehensive (Tier 6)

## Success Metrics

When done:
- ? Builds without errors/warnings
- ? Tier 1 tests pass in <100ms
- ? All tests pass
- ? Benchmarks run without error
- ? No references to old abstractions
- ? StorageInstance used consistently

## TLV Wire Format (Reference)

```
[Type:1 byte] [KeyLen:4 bytes] [ValLen:4 bytes] [Key] [Value]
     ?              ?               ?
  0x01=Put     0x04 little-endian  0x04 little-endian
  0x00=Delete
```

Example: Put key="foo", value="bar"
```
01 03 00 00 00 03 00 00 00 66 6F 6F 62 61 72
^  ^           ^           ^       ^
Put keylen=3   vallen=3   "foo"   "bar"
```

## Storage Modes at a Glance

### Local-Only Mode
```
MemTable ? LocalWAL (disk segments) + LocalSST (disk files)
```
- Perfect for: Development, testing
- Recovery: Replay WAL from disk
- Design: Traditional LSM

### Hybrid Cloud Mode
```
MemTable ? HybridCloudWAL (mem buffer ? disk ? cloud)
        + HybridCloudSST (NVMe cache + cloud)
```
- Perfect for: Production, cloud-native
- Recovery: Cloud is source of truth
- Cache: Ephemeral, LRU eviction, safe to lose

## Common Gotchas

? **Wrong**: Modify old WAL abstractions instead of deleting
? **Right**: Delete abstractions, refactor engine to use StorageInstance

? **Wrong**: Keep both old tests and new tests
? **Right**: Migrate tests to new tiers, delete old structure

? **Wrong**: Test benchmarks with same code as unit tests
? **Right**: Unit tests verify correctness, benchmarks measure perf

## Need Help?

- **Architecture**: Read `README_GRAVEL_ACTOR_LSM.md`
- **What to Delete**: Read `DELETION_ROADMAP.md`
- **Step-by-Step**: Read `EXECUTION_ROADMAP.md`
- **Checklist**: Read `IMPLEMENTATION_CHECKLIST.md`
- **Current Status**: Read `CURRENT_STATE.md`
