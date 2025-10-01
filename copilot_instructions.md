Purpose

This file contains short, actionable instructions for an automated code assistant (Copilot) working on this repository. Keep edits minimal, deterministic, and follow the project conventions below.

Project basics

- Language: C# 13
- Targets: .NET 8, .NET 9, we will keep up with releases
- Main project: `src/Gravel/Gravel.csproj`
- Tests: `test/Gravel.Tests/Gravel.Tests.csproj`
- Benchmarks: `benchmark/Gravel.Benchmark/Gravel.Benchmark.csproj`

Build / Test

- Build: `dotnet build` (at repo root or the specific project)
- Run tests: `dotnet test`
- Run benchmarks: `dotnet run -p benchmark/Gravel.Benchmark`

Code style & conventions

- Prefer explicit endianness everywhere. Use `System.Buffers.Binary.BinaryPrimitives` for reading/writing integers in little-endian.
- Avoid unnecessary allocations in hot paths. Use `stackalloc` and spans where appropriate (example: SHA-256 hash buffers).
- Keep public surface small and documented. Add XML doc comments for new public APIs.
- Use `const` for fixed sizes (example: `HeaderSize = 12` in `BloomFilter`).
- When adding serialization formats, be explicit and deterministic: define header layout and versioning.
- Follow rules specified in the repository `.editorconfig` (formatting and analyzer rules).

Serialization format (existing `BloomFilter`)

- Header: 3 x Int32 in little-endian
  - `Bits` (int32 LE)
  - `HashFunctions` (int32 LE)
  - `length` (int32 LE) — number of bytes that follow for the bit array
- Then the bit-array bytes (length bytes)
- Synchronous and asynchronous readers/writers must agree exactly on endianness and layout.

## Safety and validation

- Validate constructor and deserialized inputs (e.g., `Bits`, `HashFunctions`, buffer lengths). Throw `ArgumentOutOfRangeException` for invalid values.
- Clamp `HashFunctions` to a reasonable maximum to avoid CPU/DoS issues when reading malformed files.
- Document thread-safety: current `BloomFilter` is not thread-safe for concurrent writes. Either document or implement concurrency-safe bit sets.

## Testing guidance

- Add unit tests for serialization round trips (sync and async), including when serialized length is larger than in-memory capacity.
- Add tests for edge cases (`expectedItems = 0`, tiny or extreme false-positive rates).
- If making thread-safety changes, add concurrency tests.
- Test names now follow should_x_given_y_when_z.
- Each test uses explicit // Arrange, // Act, and // Assert sections.
- Unit tests should only test one logical behavior per test.
- Use `FluentAssertions` for assertions in tests.
- if a test has an empty catch block, add a comment explaining why it's empty

Commit / PR guidelines

- Keep changes small and well-scoped.
- Include unit tests for behavioral changes.
- Use clear commit messages: one feature/fix per commit when possible.
- For public API changes, include a short rationale in the PR description.

If you need to modify or add files, follow the repository layout and run `dotnet build` after edits to ensure no compile errors. If you want, implement one prioritized change and run the build/tests; report build/test results in the PR description.