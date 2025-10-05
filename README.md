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
  - 🔍 Sparse top-level indexes (configurable) to trade memory for a small extra seek.

- 🔍 Bloom filters
  - Per-table (or per-block) probabilistic filters with tunable false-positive rates to avoid unnecessary disk reads.

- 🏗️ Block builders & filters
  - Efficient `DataBlockBuilder`, `SimpleBlockBuilder`, `FullFilterBlockBuilder`, `RangeDeleteBlockBuilder` using pooled buffers and prefix compression.

- ⚙️ Compaction & background workers
  - Merge SSTables, apply tombstones/range deletes, produce compacted files; cancellable and observable worker tasks.

---

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

---

## 🧾 API example (conceptual)

Below is a short conceptual example demonstrating typical operations. Replace with concrete API calls from `src/Gravel` if you prefer code tied to the public surface.

```csharp
// open or create a DB (conceptual)
using var db = GravelDatabase.Open("./data");

// Insert will fail if key exists
var ok = db.Insert(keyBytes, valueBytes);
if (!ok) Console.WriteLine("key exists");

// Put overwrites
db.Put(keyBytes, valueBytes);

// Delete single key
db.Delete(keyBytes);

// Range delete
db.RangeDelete(startKeyBytes, endKeyBytes);

// Atomic batch
using var batch = db.CreateWriteBatch();
batch.Put(k1, v1);
batch.Delete(k2);
db.ApplyBatch(batch); // durable and atomic
```

## 📁 Project layout

- `src/Gravel` — core engine and implementation (memtable, writers, compaction, SSTable formats, block builders)
- 🧪 `benchmark/Gravel.Benchmarks` — microbenchmarks (BenchmarkDotNet)
- ✅ `test/Gravel.Tests` — unit tests

---

If you want the README to show concrete API signatures taken directly from the codebase, I can extract them from `src/Gravel` and update the examples to reflect the real public surface (sync or async).
