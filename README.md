# Gravel

A full-featured, production-oriented Log-Structured Merge (LSM) tree key-value store implemented in .NET — a complete embedded storage engine with reusable primitives and a focus on idiomatic, high-performance .NET code.

## 💡 Overview

Gravel implements the full LSM stack: WAL, memtable, SSTable format (blocks, indexes, filters), compaction, and runtime helpers (pools, spans, async I/O). It targets .NET 8 and uses modern C# features (`Span<T>`, `Memory<T>`, `ArrayPool<T>`) to minimize allocations and maximize throughput.

Key goals:
- ✅ Production-ready semantics and on-disk formats
- 🧰 Clear, reusable primitives for extensibility
- 📈 Measured performance with microbenchmarks

## 🔧 Core implementation

Look in `src/Gravel` for the concrete implementations. Notable components:

- 🔒 Write-Ahead Log (WAL)
  - Durable, append-only log with batched writes and replay for crash recovery.

- 🧠 MemTable
  - In-memory sorted structure optimized for fast writes/reads and flush to SSTable.

- 📦 SSTable (on-disk)
  - Writer/reader for immutable, sorted table files.
  - Block-oriented layout with restart points and block indexes.
  - Sparse top-level indexes (configurable) to trade memory for a small extra seek.
  - Bloom filters - Per-table (or per-block) probabilistic filters with tunable false-positive rates to avoid unnecessary disk reads.

- ⚙️ Compaction & background workers
  - Merge SSTables, apply tombstones/range deletes, produce compacted files; cancellable and observable worker tasks.

## ✨ Features

- ❌✅ Insert semantics
  - `Insert` is conditional and will fail if the key already exists (creation-only semantic).
  - `Put` performs an upsert (overwrite) and follows last-write-wins ordering.

- 🔁 Put, Delete, Range Delete
  - `Put`: insert or overwrite a key.
  - `Delete`: single-key tombstone; removed during compaction.
  - `Range Delete`: range tombstone semantics merged at compaction to remove keys in the range.

- 🔐 Transactions & atomic batches
  - Durable atomic batches via the WAL: append a batch atomically so either the entire batch is durable or none of it is.
  - Lightweight guarantees focused on batch atomicity and durability; full serializable transactions are not provided by default.

- ⚡ Performance-minded design
  - `Span<T>` / `Memory<T>` usage, `ArrayPool<byte>` pooling, and careful block layouts for cache efficiency.

## 🧾 API example (conceptual)

Below is a short conceptual example demonstrating typical operations. Start by creating the filesystem/store using `GravelFactory.CreateFileSystemAsync` (async). Replace later calls with concrete API calls from `src/Gravel` if you prefer code tied to the public surface.

```csharp
// conceptual async example
public static async Task Main()
{
    // create/open the storage filesystem or runtime (starting point)
    await using var store = await GravelFactory.CreateFileSystemAsync("./data");

    // operations shown conceptually; replace with real store API

    // Insert will fail if key exists
    var inserted = await store.InsertAsync(keyBytes, valueBytes);
    if (!inserted) Console.WriteLine("key exists");

    // Put overwrites
    await store.PutAsync(keyBytes, valueBytes);

    // Delete single key
    await store.DeleteAsync(keyBytes);

    // Range delete
    await store.RangeDeleteAsync(startKeyBytes, endKeyBytes);

    // Atomic batch (conceptual)
    using var batch = store.CreateWriteBatch();
    batch.Put(k1, v1);
    batch.Delete(k2);
    await store.ApplyBatchAsync(batch); // durable and atomic
}
