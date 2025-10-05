# Gravel

A full-featured, production-oriented Log-Structured Merge (LSM) tree key-value store implemented in .NET — not just building blocks, but a complete embedded storage engine you can run in your applications.

Gravel implements the whole stack required for a performant LSM engine: durable write-ahead logging, an in-memory memtable, SSTable formats with block compression and indexes, filters, range tombstones, compaction, and runtime helpers (pools, spans, and async IO). The project also exposes reusable primitives (block builders, filters, and on-disk formats) so you can reuse parts independently.

Targets: `.NET 8` (C# 12)

Highlights
- A complete embedded LSM key-value store that can be integrated into server or client applications.
- Clear, well-factored primitives for building or extending storage behaviors.
- Production-minded features: WAL, flush/compaction, range delete handling, and efficient on-disk layouts.
- Focus on idiomatic .NET performance: `ArrayPool<T>`, `Span<T>`, `Memory<T>`, and async I/O.
- Microbenchmarks included to exercise hot paths and guide optimizations.

What is an LSM tree?
An LSM (Log-Structured Merge) tree is a storage design optimized for high write throughput. Writes are appended to a WAL and applied to an in-memory memtable. When the memtable fills, it is flushed as an immutable, sorted SSTable on disk. Periodic compaction merges SSTables to reclaim space and improve read performance.

Examples of mature LSM systems: RocksDB (C++), Pebble (Go). Gravel adapts common LSM patterns to .NET and packages them as a complete engine.

Why a .NET implementation?
- Ecosystem integration: embed an LSM engine directly in .NET services without cross-process overhead.
- Productivity: faster iteration and safer abstractions while keeping performance high.
- Modern runtime: .NET 8 offers performance features (AOT, span/memory APIs, improved JIT) that make native-like performance attainable.

Core components (full engine + primitives)
- Write-Ahead Log (WAL): durable append-only log used for crash recovery and write durability.
- MemTable: in-memory sorted structure accepting writes and serving reads until flush.
- SSTable: immutable on-disk, sorted files containing data blocks, block index, filter blocks, and footer metadata.
- Block builders: `DataBlockBuilder`, `SimpleBlockBuilder`, `FullFilterBlockBuilder`, `RangeDeleteBlockBuilder` — efficient builders that produce block payloads using pooled memory.
- Filters: bloom or full-key filters to reduce unnecessary disk reads.
- Range tombstones: support for range deletes and merging during compaction.
- Compaction: background merging of SSTables to reclaim space and maintain read performance.
- Indexing: per-block and optional higher-level indexes for fast key lookup.
- Memory management: heavy use of `ArrayPool<byte>`, `Span<T>`, and `Memory<T>` to minimize allocations.

Core implementation
This repository contains complete implementations for the core LSM pieces. Look in `src/Gravel` for the concrete classes and modules that implement each area.

- WAL (Write-Ahead Log)
  - Durable, append-only log used to persist recent writes and ensure recoverability after crashes.
  - Provides batched, flushable writes and a replay path to rebuild memtables on startup.

- MemTable
  - An in-memory, sorted structure that accepts writes and serves reads until it is flushed to an SSTable.
  - Optimized for fast inserts and point/range reads; tailored to work with the WAL for durability.

- SSTable
  - Writer and reader implementations producing and consuming immutable, sorted table files.
  - Supports block-oriented data layout, restart points, block indexes, filter blocks, and file footers.
  - Includes support for on-disk range tombstones and efficient block-level reads.
  - Bloom filters: probabilistic, fast-per-key filters built per-table (or per-block) to avoid unnecessary disk IO for negative lookups. False positive rates are tunable at write time.
  - Sparse indexes: SSTable writers can emit sparse top-level indexes that map key ranges to block offsets, reducing memory footprint at the cost of a small additional disk seek during lookup.

- Compaction and background workers
  - Merge multiple SSTables, apply deletions and range tombstones, and produce new compacted SSTables.
  - Background tasks are designed to be cancellable and observable.

Features
Gravel implements the practical features you'd expect from a modern LSM-based key-value store. Below are the core capabilities and the semantics they provide.


- Insert, Put, Delete, and Range Delete
  - `Insert` is conditional: it will fail if the key already exists. This enforces an append-only or creation-only semantic where callers can ensure they do not overwrite existing keys.
  - `Put` (insert/overwrite): store or overwrite a key with the given value (upsert).
  - `Delete` (single-key tombstone): record a tombstone for a single key so compaction can drop the key.
  - `Range Delete` (range tombstone): represent deletes over a key range; tombstones are merged during compaction to remove keys within the specified ranges.
  - Range deletes are handled at write and compaction time so that scans and reads respect deleted ranges.

- Transactions and atomic batches
  - Atomic batched writes: Gravel provides durable, atomic batch semantics via the WAL and memtable flush path. A batch of puts/deletes appended together is applied atomically in the sense that either the whole batch is durable or not.
  - Lightweight transactional guarantees are focused on atomicity of batches and durability; full multi-statement, multi-key serializable transactions are not provided out-of-the-box and would need to be layered on top of the existing primitives if required.

Usage (quick)
1. Build
   - `dotnet build`

2. Run tests
   - `dotnet test`

3. Run benchmarks
   - `dotnet run -p benchmark/Gravel.Benchmarks`

Repository layout (common places to look)
- `src/Gravel` — core engine and implementation (memtable, writers, compaction, SSTable formats, block builders)
- `benchmark/Gravel.Benchmarks` — microbenchmarks (BenchmarkDotNet)
- `test/Gravel.Tests` — unit tests
