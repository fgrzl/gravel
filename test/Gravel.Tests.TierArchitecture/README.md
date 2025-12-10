# Gravel Test Suite - Tiered Architecture

## Overview

The Gravel test suite is organized into **6 tiers within a single `test/Gravel.Tests/` project**, each with specific goals and constraints:

### Tier 1: Hot Path (~100ms, <10 tests)
**Goal**: Validate critical 1-3 operation sequences
**Tests**: 
- TLV read/write hot path
- MemTable insert/get
- WAL append

### Tier 2: Subsystem (~1s, <50 tests)
**Goal**: Validate individual components in isolation
**Tests**:
- TLVReader/TLVWriter comprehensive
- LocalWAL operations
- HybridCloudWAL with actor
- LocalSSTManager I/O
- MemTable operations
- Levels management
- Actor runtime basics

### Tier 3: System (~10s, <200 tests)
**Goal**: Validate subsystems working together
**Tests**:
- Full write path (transaction ? WAL ? MemTable ? SST)
- Full read path (memtable ? Levels ? Cloud SST)
- Compaction actor integration
- Recovery from WAL/cloud
- Cache eviction behavior
- Concurrent operations

### Tier 4: Integration (~60s, <100 tests)
**Goal**: Validate end-to-end scenarios
**Tests**:
- Multi-node recovery
- Cloud storage durability
- Cache inconsistency handling
- Failure modes (crash, corruption)
- Upgrade/migration scenarios

### Tier 5: Soak (~10min, <20 tests)
**Goal**: Find memory leaks, slow degradation
**Tests**:
- Write 1M entries, verify no memory leak
- Run background compaction 24h
- Repeated crash/recovery cycles
- Cache churn behavior

### Tier 6: Capacity (~1h, <10 tests)
**Goal**: Benchmark limits and scalability
**Tests**:
- 1GB+ dataset performance
- Compaction throughput
- Cache replacement efficiency
- Multi-tenant isolation

## Running Tests

```bash
# All tests (all tiers)
dotnet test test/Gravel.Tests/

# Specific tier
dotnet test test/Gravel.Tests/ --filter "Tier1*"

# Single test
dotnet test test/Gravel.Tests/ --filter "TestClassName=TLVReaderTests"
```

## Architecture

All tests are in ONE project: `test/Gravel.Tests/`

```
test/Gravel.Tests/                    (single project)
??? Fixtures/                         (shared test fixtures)
??? Builders/                         (test builders)
??? Helpers/                          (common helpers)
?
??? Tier1Hotpath/                    (100ms, <10 tests)
?   ??? TLV/
?   ??? MemTable/
?   ??? WAL/
?
??? Tier2Subsystem/                  (1s, <50 tests)
?   ??? TLV/
?   ??? Local/
?   ??? HybridCloud/
?   ??? Actor/
?   ??? Engine/
?
??? Tier3System/                     (10s, <200 tests)
?   ??? WritePathTests.cs
?   ??? ReadPathTests.cs
?   ??? CompactionTests.cs
?   ??? RecoveryTests.cs
?   ??? ConcurrencyTests.cs
?
??? Tier4Integration/                (60s, <100 tests)
?   ??? MultiNodeTests.cs
?   ??? FailoverTests.cs
?   ??? DurabilityTests.cs
?   ??? MigrationTests.cs
?
??? Tier5Soak/                       (10min, <20 tests)
?   ??? MemoryLeakTests.cs
?   ??? DegradationTests.cs
?   ??? CrashRecoveryTests.cs
?
??? Tier6Capacity/                   (1h, <10 tests)
    ??? ScalabilityTests.cs
    ??? PerformanceTests.cs
```

## Test Categories

Each tier organizes tests by component:
- **TLV**: Zero-copy serialization
- **MemTable**: In-memory skip-list
- **WAL**: Write-ahead logging
- **Local**: LocalWAL, LocalSSTManager
- **HybridCloud**: Cloud storage modes
- **Actor**: Message dispatching
- **Engine**: DbEngine integration

## CI/CD Integration

- **PR Checks**: Tier 1 + Tier 2 (~5min)
- **Merge Gates**: Tiers 1-3 (~15min)
- **Nightly**: All tiers (~2h)
- **Release**: Tiers 1-4 + Tier 6 sample (~1h)

## Test Naming Convention

Use namespace to indicate tier:

```csharp
namespace Gravel.Tests.Tier1Hotpath.TLV;

public class TLVReaderTests
{
    [Fact]
    public void TryReadEntry_WithValidPutEntry_ReturnsTrue()
    {
        // Arrange, Act, Assert
    }
}
```

This makes it easy to filter by tier:
```bash
dotnet test --filter "FullyQualifiedName~Gravel.Tests.Tier1Hotpath"
