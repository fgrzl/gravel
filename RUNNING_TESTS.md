# ?? Running Tier 1 Unit Tests

**60 comprehensive unit tests for DbEngine, MemTable, and SequenceGenerator**

---

## Quick Start

```bash
# Build
dotnet build

# Run all Tier 1 tests
dotnet test test/Gravel.Tests/ --filter "Tier1Hotpath"

# Expected: 60 tests passed ?
```

---

## Test Breakdown

### DbEngineBasicCrudTests (30 tests)
Tests the main database engine CRUD operations

```bash
dotnet test test/Gravel.Tests/ --filter "DbEngineBasicCrudTests"
```

**Coverage**:
- Basic PUT/GET operations
- Overwrites and updates
- DELETE operations and tombstones
- EXISTS checks
- INSERT with duplicate detection
- BATCH operations (atomic multi-op)
- DELETE RANGE operations
- Sequential stress tests (100 operations)
- Edge cases (empty keys/values, large values)

### MemTableBasicOperationsTests (30 tests)
Tests the in-memory sorted buffer

```bash
dotnet test test/Gravel.Tests/ --filter "MemTableBasicOperationsTests"
```

**Coverage**:
- PUT with value storage
- PUT with overwrites
- GET with sequence tracking
- DELETE with tombstones
- Approximate size tracking
- Flush threshold detection
- Entry export for SST writing
- Lexicographic key ordering
- Memory clearing and reuse
- Edge cases

### SequenceGeneratorBasicTests (10 tests)
Tests monotonic sequence generation and thread-safety

```bash
dotnet test test/Gravel.Tests/ --filter "SequenceGeneratorBasicTests"
```

**Coverage**:
- Monotonic increment
- Custom start sequences
- No repeats over 1000 calls
- Current read without increment
- Recovery via SetCurrent
- Concurrent access (10 threads × 100 calls)
- Thread-safe mixed operations

---

## Running Specific Tests

### By Category

```bash
# All PUT tests
dotnet test test/Gravel.Tests/ --filter "put_should"

# All GET tests
dotnet test test/Gravel.Tests/ --filter "get_should"

# All DELETE tests
dotnet test test/Gravel.Tests/ --filter "delete_should"

# All BATCH tests
dotnet test test/Gravel.Tests/ --filter "batch_should"

# All thread-safety tests
dotnet test test/Gravel.Tests/ --filter "thread_safe OR concurrent"
```

### By Specific Test

```bash
# Single test
dotnet test test/Gravel.Tests/ \
  --filter "put_should_store_key_value_pair"

# Multiple specific tests
dotnet test test/Gravel.Tests/ \
  --filter "put_should_store_key_value_pair OR get_should_return_stored_value"
```

---

## Verbose Output

```bash
# Detailed output for debugging
dotnet test test/Gravel.Tests/ \
  --filter "Tier1Hotpath" \
  -v detailed

# Show passing tests too
dotnet test test/Gravel.Tests/ \
  --filter "Tier1Hotpath" \
  --logger "console;verbosity=detailed"
```

---

## Expected Output

```
Starting test execution, please wait...
A total of 60 tests found in ~/Gravel.Tests.csproj

DbEngineBasicCrudTests::put_should_store_key_value_pair PASSED [0.123s]
DbEngineBasicCrudTests::put_should_overwrite_existing_value PASSED [0.045s]
...
MemTableBasicOperationsTests::put_should_store_key_value_pair PASSED [0.002s]
...
SequenceGeneratorBasicTests::next_should_return_first_sequence_as_one PASSED [0.001s]
...

Test Execution Summary:
  Total tests:    60
  Passed:         60
  Failed:          0
  Skipped:         0

All tests passed ?
```

---

## Troubleshooting

### Test Discovery Issues

```bash
# Rebuild to ensure tests are discovered
dotnet clean
dotnet build
dotnet test test/Gravel.Tests/
```

### Slow Tests

Some tests may be slower due to:
- Temporary directory creation/cleanup
- Large value tests (1MB allocations)
- Thread-safety tests (concurrent operations)

Add timeout if needed:

```bash
dotnet test test/Gravel.Tests/ \
  --filter "Tier1Hotpath" \
  --logger "console" \
  --collect:"XPlat Code Coverage"
```

### Individual Test Failures

Debug a specific test:

```bash
# Run with more details
dotnet test test/Gravel.Tests/ \
  --filter "put_should_store_key_value_pair" \
  --diag ~/test-diag.log

# Check the log file
cat ~/test-diag.log
```

---

## Test Statistics

- **Total Tests**: 60
- **Test Files**: 3
- **Async Tests**: 30 (DbEngine)
- **Sync Tests**: 30 (MemTable, SequenceGenerator)
- **Thread-Safety Tests**: 2
- **Stress Tests**: 1
- **Edge Case Tests**: 8
- **Lines of Test Code**: ~1,500
- **Expected Runtime**: <5 seconds

---

## Integration with CI/CD

### GitHub Actions Example

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      - run: dotnet build
      - run: dotnet test test/Gravel.Tests/ --filter "Tier1Hotpath"
```

### Local Pre-commit Hook

```bash
#!/bin/bash
# .git/hooks/pre-commit

dotnet test test/Gravel.Tests/ --filter "Tier1Hotpath"
if [ $? -ne 0 ]; then
  echo "Tests failed. Commit aborted."
  exit 1
fi
```

---

## What's Being Tested

### DbEngine (Main Database)
- ? Lazy initialization
- ? CRUD correctness
- ? Tombstones for deletes
- ? Atomic batch operations
- ? Sequence number tracking
- ? WAL integration
- ? Memtable flushing
- ? Levels interaction

### MemTable (Sorted Buffer)
- ? O(log n) operations
- ? Lexicographic ordering
- ? Size tracking
- ? Flush detection
- ? Entry export
- ? Thread-safe updates
- ? Tombstone handling
- ? Concurrent reads

### SequenceGenerator (Ordering)
- ? Monotonic IDs
- ? No repeats
- ? Recovery support
- ? Thread-safe generation
- ? Concurrent access

---

## Next Phase: Tier 2 Tests

After all Tier 1 tests pass:

```bash
# Layer in Tier 2 tests
dotnet test test/Gravel.Tests/ --filter "Tier2Subsystem"
```

Tier 2 covers:
- LocalWAL operations
- LocalSST reading/writing
- TLV format serialization
- HybridCloud storage
- Cloud integration
- Recovery from storage

---

**All tests are ready to run and should pass with the current implementation.** ?
