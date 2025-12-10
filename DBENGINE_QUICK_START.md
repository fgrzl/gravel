# DbEngine Quick Start Guide

## Creating an Engine Instance

```csharp
using Gravel.Engine;
using Gravel.Storage;
using Microsoft.Extensions.Logging;

// Setup
var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<DbEngine>();
var config = new StorageConfig
{
    Mode = StorageMode.LocalOnly,
    LocalPath = "/data/gravel",
    WalSegmentSizeBytes = 10 * 1024 * 1024  // 10 MB
};

// Create storage
var runtime = null; // Actor runtime (optional)
var storage = StorageFactory.Create(config, runtime, logger);

// Create engine
var engine = new DbEngine(storage, logger);
```

## Basic Operations

### Write (Put)
```csharp
await engine.PutAsync("mykey"u8.ToArray(), "myvalue"u8.ToArray());
```

### Read (Get)
```csharp
var value = await engine.GetAsync("mykey"u8.ToArray());
if (value.HasValue)
    Console.WriteLine($"Value: {Encoding.UTF8.GetString(value.Value.Span)}");
```

### Delete
```csharp
bool deleted = await engine.DeleteAsync("mykey"u8.ToArray());
Console.WriteLine($"Deleted: {deleted}");
```

### Range Delete
```csharp
await engine.DeleteRangeAsync("key1"u8.ToArray(), "key9"u8.ToArray());
```

### Batch Operations
```csharp
var mutations = new List<Mutation>
{
    new Mutation { Op = MutationOp.Put, Key = "k1"u8.ToArray(), Value = "v1"u8.ToArray() },
    new Mutation { Op = MutationOp.Put, Key = "k2"u8.ToArray(), Value = "v2"u8.ToArray() },
    new Mutation { Op = MutationOp.Delete, Key = "k3"u8.ToArray() }
};

await engine.BatchAsync(mutations);
```

### Check Existence
```csharp
bool exists = await engine.ExistsAsync("mykey"u8.ToArray());
```

## Cleanup

```csharp
// Flushes any pending data and closes storage
await engine.DisposeAsync();
```

---

## How It Works

### Write Flow
1. **Sequence Assignment** - SequenceGenerator.Next() assigns order #
2. **WAL Append** - CloudNativeWAL writes to disk before memory
3. **MemTable Insert** - Entry inserted into sorted memtable
4. **Size Check** - If memtable > 64MB, triggers flush
5. **SST Write** - Memtable written to SST file, MemTable reset
6. **Return** - Client gets response immediately

### Read Flow
1. **MemTable Check** - Fast lookup in sorted dictionary
   - If found (not deleted), return immediately
   - If deleted (tombstone), treat as miss
2. **Levels Search** - If not in memtable, scan L0?L9
   - Stops at first match
   - Returns null if not found anywhere

### Flush Flow
1. **Get All Entries** - MemTable.GetEntries()
2. **Convert Format** - DbEntry ? TLV tuples
3. **Write SST** - Disk or cloud-based
4. **Register Level** - Levels[0].Add(new SSTFileInfo)
5. **Reset MemTable** - Create new empty memtable

---

## Configuration

### LocalOnly Mode
```csharp
new StorageConfig
{
    Mode = StorageMode.LocalOnly,
    LocalPath = "/data/gravel",
    WalSegmentSizeBytes = 10 * 1024 * 1024
}
```

### HybridCloud Mode
```csharp
new StorageConfig
{
    Mode = StorageMode.HybridCloud,
    LocalPath = "/cache/gravel",
    CloudStorage = new S3CloudStorage(...),
    CloudBucket = "my-gravel-bucket",
    LocalCacheSizeBytes = 100 * 1024 * 1024,  // 100 MB
    WalSegmentSizeBytes = 10 * 1024 * 1024
}
```

---

## Monitoring

### Enable Logging
```csharp
var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Debug);
});
var logger = loggerFactory.CreateLogger<DbEngine>();
```

### Collect Metrics
```csharp
// Subscribe to OpenTelemetry meters
var meterListener = new MeterListener();
meterListener.AddMeter("Gravel");
meterListener.InstrumentPublished += (instrument, listener) =>
{
    // Record metrics
};
```

### Enable Tracing
```csharp
// Subscribe to OpenTelemetry activities
var activityListener = new ActivityListener();
activityListener.SampleUsingParentState = (ref ActivityCreationOptions<ActivityContext> _) =>
    ActivitySamplingResult.AllData;

ActivitySource.AddActivityListener(activityListener);
```

---

## Common Patterns

### Bulk Loading
```csharp
for (int i = 0; i < 1_000_000; i++)
{
    await engine.PutAsync(
        Encoding.UTF8.GetBytes($"key-{i:D10}"),
        Encoding.UTF8.GetBytes($"value-{i}")
    );
}
// Auto-flushes memtable when it hits 64MB
```

### Conditional Update
```csharp
var existing = await engine.GetAsync(key);
if (existing == null)
{
    await engine.PutAsync(key, newValue);
}
```

### Atomic Transaction
```csharp
var mutations = new List<Mutation>
{
    // Prepare all changes
};
await engine.BatchAsync(mutations);  // All or nothing
```

### Point-in-time Snapshot
```csharp
// All Get operations see consistent view
// based on sequence number at MemTable.Put
```

---

## Troubleshooting

### Engine Not Initialized
```csharp
// Auto-initialization happens on first operation
// Or explicitly:
await engine.InitializeAsync();
```

### Key Already Exists
```csharp
try
{
    await engine.InsertAsync(key, value);
}
catch (InvalidOperationException)
{
    // Key already exists
    await engine.PutAsync(key, newValue);  // Update instead
}
```

### Memory Growing
```csharp
// Memtable flushes automatically at 64MB
// If still growing, check if storage is writable
// or if cloud uploads are blocked
```

### Storage Not Available
```csharp
// LocalOnly mode works even without cloud
// HybridCloud mode requires cloud storage connection
```

---

## Performance Tips

1. **Batch Operations** - Use BatchAsync for multiple writes
2. **Key Size** - Smaller keys = faster memtable lookups
3. **Value Size** - Doesn't matter much (stored opaquely)
4. **Concurrent Access** - Use async patterns, not blocking
5. **Monitoring** - Enable metrics to track flush patterns

---

## Architecture Reference

```
DbEngine
?? MemTable (sorted buffer)
?  ?? Put/Get/Delete ops
?? Levels (SST hierarchy)
?  ?? L0 (fresh flushes)
?  ?? L1-L9 (compacted)
?? CloudNativeWAL (persist)
?  ?? Memory buffer (fast)
?  ?? Cloud upload (async)
?? SequenceGenerator
   ?? Monotonic ordering
```

---

**Ready to use! Start with LocalOnly mode for testing, switch to HybridCloud for production.** ??
