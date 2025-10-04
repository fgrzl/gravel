# Gravel TODO

This file contains a prioritized list of tasks, improvements and ideas for the Gravel embedded LSM project. Use it as a living checklist for development and performance work.

## High priority

- [ ] Add benchmark suite (YCSB-style) and CI job to run basic read/write/mix workloads.
- [ ] Add CI build and test pipeline (cover Debug/Release, net8.0) and run unit tests.
- [ ] Add basic diagnostics/metrics (counters for memtable flushes, compactions, WAL bytes, SST count).
- [ ] Run crash and recovery tests (WAL rollover and compaction interruptions) and harden recovery paths.

## Performance & profiling

- [ ] Create microbenchmarks for hot paths: Put, Get, Range scan, MemTable Scan.
- [ ] Profile under load to identify allocation hotspots and GC pressure (dotnet-trace/dotnet-counters).
- [ ] Reduce allocations: revisit `ToArray()` hot paths, snapshot copies in `MemTable.Scan`, and block parsing.
- [ ] Add block cache and measure read amplification.

## Concurrency & scalability

- [ ] Evaluate sharded memtables or lock-free data structures to reduce single-lock contention.
- [ ] Support concurrent memtable flushes and parallel compaction workers.
- [ ] Add fine-grained locking for reads where possible.

## IO, storage format & caching

- [ ] Add optional memory-mapped SST reader path for low-latency reads (platform-conditional).
- [ ] Implement or expose block cache for SST data blocks.
- [ ] Reuse pooled buffers for SST read/write to avoid repeated allocations.

## Compaction & background work

- [ ] Add multi-threaded compaction scheduling and prioritization.
- [ ] Improve compaction heuristics (size/score based, dynamic leveling, reduce write amplification).
- [ ] Add more tests for range-tombstone interaction across levels and multi-file compactions.

## Reliability & testing

- [ ] Fuzz WAL and SST parsing to find deserialization bugs.
- [ ] Add tests for partial/truncated SST and CRC failure handling.
- [ ] Add long-running stress tests that simulate concurrent clients.

## Observability

- [ ] Expose metrics via an interface (Prometheus-friendly) and wire basic counters (compaction time, bytes written, wal syncs).
- [ ] Add structured logging for key lifecycle events and compaction decisions.

## Packaging, docs & developer experience

- [ ] Create a `README` section describing architecture and public extension points (IWalFactory, ISstFactory, compressors).
- [ ] Add examples showing embedding, configuration, and migration steps.
- [ ] Publish NuGet package with semantic versioning and changelog.

## Nice-to-have / long-term ideas

- [ ] Column families or logical namespaces.
- [ ] Snapshot/isolation features for consistent reads.
- [ ] TTL and space reclaim policies.
- [ ] Advanced table properties and tunables (bloom filter sizing per table, compression per-level).

## Low priority / Research

- [ ] Evaluate native interop (pinned buffers, Span-friendly compress/decompress) to minimize copies.
- [ ] Compare against RocksDB/Pebble/Badger with benchmark results and document trade-offs.

