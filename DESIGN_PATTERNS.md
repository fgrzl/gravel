# Design Patterns & Best Practices Guide

This guide explains the design patterns and best practices used in the actor-based LSM implementation.

## 1. Actor Model Pattern

### Overview
The actor model centralizes all state mutations through a single sequential runtime, eliminating race conditions and enabling deterministic behavior.

### Usage
```csharp
// Create runtime
var runtime = new ActorRuntime(logger);

// Post messages asynchronously
await runtime.PostMessageAsync(new FlushMemTableMessage { ... });

// Messages are processed in strict FIFO order
// Each message may enqueue one or more tasks
await runtime.EnqueueTaskAsync(task);

// Wait for all work to complete
await runtime.WaitForQuiesceAsync();
```

### Benefits
- **Thread-safe**: No locks needed; sequential processing guarantees safety
- **Deterministic**: Same inputs always produce same execution sequence
- **Composable**: Messages and tasks can be hierarchical
- **Debuggable**: Intent log shows exact execution history

### When to Use
- Any operation that needs deterministic ordering (compaction, flush, uploads)
- Background work that might fail and need retry logic
- Scenarios where you need to audit/replay operations

## 2. Pluggable Abstraction Pattern

### Overview
Interfaces without concrete implementations allow swapping backends without changing application code.

### Usage
```csharp
// Define interface (cloud-agnostic)
public interface ICloudStorage { ... }

// Implement for S3
public class S3CloudStorage : ICloudStorage { ... }

// Implement for Azure
public class AzureBlobStorage : ICloudStorage { ... }

// Use polymorphically
public class WalManager
{
    public WalManager(ICloudStorage storage) { ... }
}

// Switch implementations at configuration time
var storage = Environment.GetEnvironmentVariable("USE_S3") == "true"
    ? (ICloudStorage)new S3CloudStorage(awsOptions)
    : new AzureBlobStorage(azureOptions);
```

### Benefits
- **Testability**: Inject mock/stub implementations for testing
- **Flexibility**: Support multiple backends without code changes
- **Decoupling**: Application code doesn't depend on specific cloud provider
- **Migration**: Switch providers without modifying business logic

### When to Use
- Any pluggable component (storage, network, crypto, compression)
- Cross-cutting concerns (logging, telemetry, caching)
- Infrastructure abstractions (cloud, file system, database)

## 3. No-Op Implementation Pattern

### Overview
Stub implementations satisfy interfaces while doing minimal or nothing work, useful for local-only scenarios.

### Usage
```csharp
// No-op: local-only, no cloud
var wal = new NoOpCloudWalManager();

// Real: cloud-backed
var wal = new CloudWalManager(cloudStorage, ...);

// Same interface, different behavior
public async ValueTask FlushSegmentAsync(CancellationToken ct)
{
    // No-op: just returns
    // Real: uploads to cloud
}
```

### Benefits
- **Zero setup**: Works without external dependencies or configuration
- **API compatibility**: Same interface as real implementation
- **Testing**: Can test against no-op without cloud infrastructure
- **Development**: Work offline during development

### When to Use
- Features that are optional (cloud backup, replication)
- Operations that can be deferred (non-critical uploads)
- Development/testing environments

## 4. Retry Pattern with Exponential Backoff (Future)

### Overview
Tasks can specify retry logic with policies for transient vs permanent failures.

### Usage
```csharp
public class UploadWalSegmentTask : IActorTask
{
    int _retryCount;
    
    public async ValueTask<bool> HandleFailureAsync(Exception ex, CancellationToken ct)
    {
        _retryCount++;
        
        // Retry transient errors up to N times
        if (ex is TimeoutException or IOException)
            return _retryCount < 5;
        
        // Never retry permanent errors
        if (ex is FileNotFoundException)
            return false;
        
        return false;
    }
}
```

### Benefits
- **Resilience**: Automatic recovery from transient failures
- **Configurability**: Per-task retry policies
- **Traceability**: Failure tracking in intent log
- **Observability**: Can monitor and alert on retry patterns

### When to Use
- Network operations (uploads, downloads, API calls)
- Operations with temporary resource contention
- Any operation where "retry" is a valid strategy

## 5. Immutable Message Pattern

### Overview
Messages are immutable data structures carrying information about what happened, enabling safe sharing across threads.

### Usage
```csharp
public sealed class FlushMemTableMessage : ActorMessage
{
    // All properties are init-only (immutable after creation)
    public ulong UpToSequence { get; init; }
    public int EntryCount { get; init; }
    
    // Can be safely posted to queue without synchronization
    await runtime.PostMessageAsync(msg);
}

// Create with object initializer
var msg = new FlushMemTableMessage 
{ 
    EntryCount = 100, 
    UpToSequence = 50 
};
```

### Benefits
- **Thread-safe**: Can be shared without locks
- **Functional**: Same message always produces same behavior
- **Auditable**: Message state can't change after creation
- **Cloning**: Can create variants without full copy

### When to Use
- Data structures passed between threads/actors
- Events or commands in event sourcing
- Request/response messages in distributed systems

## 6. Intent Log Pattern

### Overview
Log decisions and operations as structured entries for auditability, debugging, and recovery.

### Usage
```csharp
// Intent log entries recorded by runtime
public sealed class ActorIntentLogEntry
{
    public ActorMessage SourceMessage { get; init; }
    public IActorTask ScheduledTask { get; init; }
    public ActorIntentStatus Status { get; init; }
    public Dictionary<string, object?> Metadata { get; init; }
}

// Query after execution
var log = runtime.GetIntentLog();
foreach (var entry in log)
{
    if (entry.Status == ActorIntentStatus.Failed)
        Console.WriteLine($"Failed: {entry.ErrorMessage}");
}

// Replay for recovery
foreach (var entry in log.Where(e => e.Status == ActorIntentStatus.InProgress))
{
    await recoveryHandler.ReplayAsync(entry);
}
```

### Benefits
- **Auditability**: Complete record of operations
- **Recovery**: Can resume from failures using log
- **Debugging**: Understand exactly what happened
- **Determinism**: Can verify same sequence for same inputs

### When to Use
- Critical operations (transactions, compaction, backups)
- Systems requiring auditability (compliance, security)
- Distributed systems needing consistency verification

## 7. Composite Task Pattern

### Overview
Complex operations decompose into multiple simpler tasks that execute through the actor.

### Usage
```csharp
// Flush operation decomposes into:
// 1. Write SST to local disk
// 2. Upload SST to cloud (async, retryable)
// 3. Update manifest (async, retryable)

public class FlushMemTableTask : IActorTask
{
    public async ValueTask OnCompleteAsync(CancellationToken ct)
    {
        // After flush completes, schedule upload
        var uploadMsg = new UploadWalSegmentMessage { ... };
        await runtime.PostMessageAsync(uploadMsg);
    }
}
```

### Benefits
- **Modularity**: Each task handles one concern
- **Reusability**: Same task used in different flows
- **Monitoring**: Track each step independently
- **Resilience**: Failure at one step doesn't block others

### When to Use
- Multi-step operations
- Operations with optional steps (conditional uploads)
- Workflows with interdependencies

## 8. Dependency Injection Pattern

### Overview
Constructor injection passes dependencies, enabling testability and flexibility.

### Usage
```csharp
public class WalManager
{
    readonly ICloudWalManager _cloud;
    readonly ILogger<WalManager> _logger;
    
    public WalManager(ICloudWalManager cloud, ILogger<WalManager> logger)
    {
        _cloud = cloud;
        _logger = logger;
    }
}

// Inject in tests
var mockCloud = new Mock<ICloudWalManager>();
var logger = NullLogger<WalManager>.Instance;
var manager = new WalManager(mockCloud.Object, logger);

// Inject in production
var realCloud = new S3CloudStorage(options);
var prodLogger = loggerFactory.CreateLogger<WalManager>();
var prodManager = new WalManager(realCloud, prodLogger);
```

### Benefits
- **Testability**: Inject mocks/stubs for unit testing
- **Flexibility**: Change implementations without code changes
- **Observability**: Inject loggers, telemetry providers
- **SOLID**: Depends on abstractions, not concretions

### When to Use
- Any class with dependencies
- Classes that need to be tested
- Infrastructure code

## 9. Async/Await with ValueTask

### Overview
Use ValueTask for hot-path operations, Task for most others.

### Usage
```csharp
// Hot path: small, frequently-called operations
public ValueTask AppendAsync(DbEntry entry, CancellationToken ct)
{
    // Often completes synchronously; use ValueTask to avoid allocation
    if (_buffer.TryAdd(entry))
        return ValueTask.CompletedTask;
    
    return new ValueTask(FlushAndAppendAsync(entry, ct));
}

// Cold path: less frequent operations
public async Task UploadAsync(Stream data, CancellationToken ct)
{
    // Always async; Task is fine
    await _cloudStorage.UploadAsync(data, ct);
}
```

### Benefits
- **Performance**: ValueTask avoids heap allocation when possible
- **Consistency**: Async all the way for safety
- **Cancellation**: CancellationToken enables graceful shutdown

### When to Use
- Hot-path operations (appends, gets, puts)
- Cold-path operations use Task

## 10. Composition Root Pattern

### Overview
Single place where all dependencies are created and wired together.

### Usage
```csharp
// Composition root: all dependencies created here
public static class GravelFactory
{
    public static IDbEngine Create(GravelOptions options)
    {
        // Create cloud implementations
        var cloudStorage = options.UseCloud
            ? (ICloudStorage)new S3CloudStorage(options.AwsOptions)
            : new NoOpCloudStorage();
        
        // Create managers
        var wal = new CloudWalManager(cloudStorage, ...);
        var sst = new CloudSstManager(cloudStorage, ...);
        var manifest = new CloudManifestManager(cloudStorage, ...);
        
        // Create actor runtime
        var runtime = new ActorRuntime(logger);
        
        // Create engine with all dependencies
        return new DbEngine(...);
    }
}

// Usage: single point of configuration
var engine = GravelFactory.Create(options);
```

### Benefits
- **Maintainability**: All wiring in one place
- **Consistency**: Same configuration everywhere
- **Testability**: Easy to create test variants
- **Flexibility**: Change implementations globally

### When to Use
- Application startup
- Test fixture setup
- Any time dependencies are created

## Best Practices Summary

| Practice | Why | When |
|----------|-----|------|
| Use abstractions | Enables testing and swapping implementations | Whenever there are multiple implementations |
| Immutable messages | Thread-safe, auditability | All actor messages |
| Async/await | Scalable, responsive | All I/O operations |
| Dependency injection | Testability, flexibility | Any class with dependencies |
| Intent logging | Auditability, recovery, debugging | Critical operations |
| No-op implementations | Offline development, simpler testing | Optional features |
| Composite tasks | Modularity, reusability | Multi-step operations |
| FIFO processing | Determinism, debugging | Actor runtime |
| Structured concurrency | Safety, cancellation support | All async code |
| ValueTask optimization | Performance on hot paths | Frequently-called operations |

## Anti-Patterns to Avoid

? **Don't**: Use shared mutable state in background tasks  
? **Do**: Pass state through messages and let actor manage it

? **Don't**: Start background work without enqueueing through actor  
? **Do**: Always post messages/tasks to runtime for sequencing

? **Don't**: Block async code with `.Wait()` or `.Result`  
? **Do**: Use `await` all the way up

? **Don't**: Ignore CancellationToken in async methods  
? **Do**: Pass token to all async calls

? **Don't**: Swallow exceptions silently  
? **Do**: Log failures and let intent log record them

---

These patterns create a robust, testable, maintainable codebase while leveraging modern .NET async/await and dependency injection best practices.
