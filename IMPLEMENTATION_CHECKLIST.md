# Implementation Checklist

## ? Completed

### Core Storage
- [x] TLV format (9-byte wire: type + key len + val len + data)
- [x] TLVReader (zero-copy, streaming)
- [x] TLVWriter (buffered, async)
- [x] CloudNativeSSTWriter (TLV + index + metadata)
- [x] LocalWAL (disk segments, auto-roll)
- [x] LocalSSTManager (file I/O)
- [x] HybridCloudWAL (memory buffer ? cloud)
- [x] HybridCloudSSTManager (LRU cache)
- [x] StorageFactory (mode selection)

### Actor Integration
- [x] Actor runtime (existing, reused)
- [x] ActorAwareWALManager (WAL ? actor bridge)
- [x] UploadWalSegmentMessage (async upload)

### Documentation
- [x] STORAGE_ARCHITECTURE.md
- [x] COMPLETION_SUMMARY.md
- [x] ARCHITECTURE_DIAGRAMS.md
- [x] REFACTORING_PLAN.md
- [x] REFACTORING_STATUS.md
- [x] EXECUTION_ROADMAP.md
- [x] README_GRAVEL_ACTOR_LSM.md
- [x] test/Gravel.Tests.TierArchitecture/README.md
- [x] benchmark/Gravel.Benchmarks/BENCHMARK_ARCHITECTURE.md

### Test Structure
- [x] test/Gravel.Tests.Tier1Hotpath/ (xunit project)
- [x] test/Gravel.Tests.Tier2Subsystem/ (xunit project)
- [x] test/Gravel.Tests.Tier3System/ (xunit project)
- [x] test/Gravel.Tests.Tier4Integration/ (xunit project)
- [x] test/Gravel.Tests.Tier5Soak/ (xunit project)
- [x] test/Gravel.Tests.Tier6Capacity/ (xunit project)
- [x] Initial skeleton tests in each tier

### Benchmark Structure
- [x] benchmark/Gravel.Benchmarks/Tier1Hotpath/TLVBenchmarks.cs
- [x] benchmark/Gravel.Benchmarks/Tier2Subsystem/LocalWALBenchmarks.cs
- [x] benchmark/Gravel.Benchmarks/Tier3System/WritePathBenchmarks.cs

### Legacy Removal
- [x] Delete src/Gravel/Storage/FileSystem/Wal/*
- [x] Delete src/Gravel/Storage/FileSystem/Sst/*
- [x] Delete src/Gravel/Abstractions/Storage/Sst/ISstFactory.cs
- [x] Delete src/Gravel/Abstractions/Storage/Sst/ISstWriter.cs
- [x] Delete src/Gravel/Abstractions/Storage/Sst/ISstReader.cs
- [x] Delete src/Gravel/Abstractions/Storage/Sst/SstOptions.cs
- [x] Delete src/Gravel/Abstractions/Storage/Sst/IBlockCompressor.cs

## ? In Progress / To Do

### Phase 1: Legacy Removal
- [ ] Delete src/Gravel/Abstractions/Storage/Wal/IWalFactory.cs
- [ ] Delete src/Gravel/Abstractions/Storage/Wal/IWalWriter.cs
- [ ] Delete src/Gravel/Abstractions/Storage/Wal/IWalReader.cs
- [ ] Delete src/Gravel/Abstractions/Storage/Wal/WalConstants.cs
- [ ] Delete src/Gravel/Abstractions/Storage/Wal/WalRecord.cs
- [ ] Delete src/Gravel/Abstractions/Storage/Wal/WalOptions.cs
- [ ] Delete src/Gravel/Storage/InMemory/* (in-memory implementations)
- [ ] Delete src/Gravel/Storage/Shared/* (block builders)
- [ ] Simplify/remove src/Gravel/Compression/*

### Phase 2: Core Engine Refactoring
- [ ] Refactor src/Gravel/Engine/Managers/WalManager.cs (use StorageInstance)
- [ ] Refactor src/Gravel/Engine/Managers/SstManager.cs (use StorageInstance)
- [ ] Refactor src/Gravel/Engine/DbEngine.cs (remove old abstractions)
- [ ] Update DI/configuration to use StorageFactory

### Phase 3: Build Verification
- [ ] dotnet build (no errors)
- [ ] dotnet build (no warnings)
- [ ] Verify no references to old abstractions

### Phase 4: Test Implementation
- [ ] Implement Tier 1 tests (10 tests, <100ms)
- [ ] Implement Tier 2 tests (50 tests, <1s)
- [ ] Implement Tier 3 tests (200 tests, <10s)
- [ ] Implement Tier 4 tests (100 tests, <60s)
- [ ] Implement Tier 5 tests (20 tests, <10min)
- [ ] Implement Tier 6 tests (10 tests, <1h)
- [ ] Remove old test/Gravel.Tests/ (after migration)

### Phase 5: Benchmark Implementation
- [ ] Implement Tier 1 benchmarks (5-10)
- [ ] Implement Tier 2 benchmarks (10-15)
- [ ] Implement Tier 3 benchmarks (5-10)
- [ ] Implement Tier 4 benchmarks (3-5)
- [ ] Implement Tier 5 benchmarks (2-3)
- [ ] Implement Tier 6 benchmarks (1-2)

### Phase 6: Validation
- [ ] dotnet test test/Gravel.Tests.Tier1Hotpath/ (pass, <100ms)
- [ ] dotnet test test/Gravel.Tests.Tier1Hotpath/ test/Gravel.Tests.Tier2Subsystem/ (pass, <2s)
- [ ] dotnet test test/Gravel.Tests* (all pass)
- [ ] dotnet run -c Release -p benchmark/Gravel.Benchmarks/ (runs without error)
- [ ] Verify performance expectations

## ?? Success Criteria

When complete, the following should be true:

### Code Quality
- ? No references to old WAL/SST abstractions
- ? StorageInstance used consistently
- ? All TLV operations zero-copy
- ? No RocksDB-like legacy code
- ? Clean separation of concerns

### Performance
- ? TLV read/write <1µs per entry
- ? WAL append <100µs (memory buffer)
- ? MemTable insert/get <10µs
- ? Actor dispatch <1ms

### Testing
- ? Tier 1 passes in <100ms
- ? Tier 2 passes in <1s
- ? Tier 3 passes in <10s
- ? Coverage of all critical paths

### Documentation
- ? Architecture documented
- ? Design decisions documented
- ? Migration path documented
- ? Benchmarking guide documented

## ?? Command Reference

```bash
# Build
dotnet build

# Test
dotnet test test/Gravel.Tests.Tier1Hotpath/
dotnet test test/Gravel.Tests.Tier1Hotpath/ test/Gravel.Tests.Tier2Subsystem/
dotnet test test/Gravel.Tests*/

# Benchmark
dotnet run -c Release -p benchmark/Gravel.Benchmarks/

# Benchmark specific tier
dotnet run -c Release -p benchmark/Gravel.Benchmarks/ -- --filter "*Tier1*"

# Clean
dotnet clean
```

## ?? Notes

- All benchmarks are in ONE executable (`benchmark/Gravel.Benchmarks/`)
- Tests are in SEPARATE projects (one per tier: `test/Gravel.Tests.Tier*/`)
- Tests verify CORRECTNESS, Benchmarks measure PERFORMANCE
- Tiered structure enables fast feedback (Tier 1: 100ms) without sacrificing completeness
