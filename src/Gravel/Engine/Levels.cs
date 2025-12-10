using Gravel.Abstractions;
using Gravel.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gravel.Engine;

/// <summary>
///     Manages LSM levels (L0, L1, L2, ...) for SST files.
///     Handles level organization, compaction candidates, and recovery.
/// </summary>
public sealed class Levels
{
    readonly StorageInstance _storage;
    readonly ILogger _logger;
    readonly List<List<SSTFileInfo>> _levels;
    const int MaxLevels = 10;
    const int L0CompactionThreshold = 4; // Compact L0 when 4+ files

    /// <summary>
    ///     Initializes a new <see cref="Levels" /> instance.
    /// </summary>
    public Levels(StorageInstance storage, ILogger? logger = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? NullLogger.Instance;
        _levels = new List<List<SSTFileInfo>>(MaxLevels);

        for (int i = 0; i < MaxLevels; i++)
        {
            _levels.Add(new List<SSTFileInfo>());
        }
    }

    /// <summary>
    ///     Loads existing SST files from storage into levels.
    /// </summary>
    public async ValueTask LoadAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Loading SST levels from storage");

        // For now, just initialize empty levels
        // In production, would scan storage for existing SSTs
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Checks if a key exists in any level.
    /// </summary>
    public async ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        // Scan levels from L0 to highest
        for (int level = 0; level < _levels.Count; level++)
        {
            foreach (var sst in _levels[level])
            {
                // In production, would use bloom filters and binary search
                if (await sst.ExistsAsync(key, ct))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Gets a value from levels (searches L0 to highest, stops at first hit).
    /// </summary>
    public async ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
    {
        // Scan levels from L0 to highest (L0 may have overlaps)
        for (int level = 0; level < _levels.Count; level++)
        {
            foreach (var sst in _levels[level])
            {
                var value = await sst.GetAsync(key, ct);
                if (value.HasValue)
                    return value.Value;
            }
        }

        return null;
    }

    /// <summary>
    ///     Writes a set of entries as a new SST file at the specified level.
    /// </summary>
    public async ValueTask<string> WriteSSTAsync(IEnumerable<DbEntry> entries, int level, CancellationToken ct = default)
    {
        if (level < 0 || level >= MaxLevels)
            throw new ArgumentOutOfRangeException(nameof(level));

        _logger.LogInformation("Writing SST to level {Level}", level);

        // Convert DbEntry to TLV tuple format
        async IAsyncEnumerable<(byte Type, System.ReadOnlyMemory<byte> Key, System.ReadOnlyMemory<byte> Value, ulong Sequence)> ConvertEntries(IEnumerable<DbEntry> dbEntries)
        {
            foreach (var entry in dbEntries)
            {
                var type = entry.Kind switch
                {
                    DbEntryKind.Put => Gravel.Storage.TLV.TLVFormat.TypePut,
                    DbEntryKind.DeleteKey => Gravel.Storage.TLV.TLVFormat.TypeDelete,
                    DbEntryKind.DeleteRange => Gravel.Storage.TLV.TLVFormat.TypeDeleteRange,
                    _ => throw new InvalidOperationException($"Unknown entry kind: {entry.Kind}")
                };

                yield return (type, entry.Key, entry.Value, entry.Sequence);
                await Task.Yield();
            }
        }

        // Use storage to write SST based on mode
        string path;
        if (_storage.Mode == StorageMode.LocalOnly)
        {
            var sstMgr = _storage.LocalSST ?? throw new InvalidOperationException("LocalSST not initialized");
            path = await sstMgr.WriteAsync(ConvertEntries(entries.ToList()), ct: ct);
        }
        else
        {
            var sstMgr = _storage.HybridSST ?? throw new InvalidOperationException("HybridSST not initialized");
            path = await sstMgr.WriteAsync(ConvertEntries(entries.ToList()), ct: ct);
        }

        // Register with levels
        var info = new SSTFileInfo
        {
            Path = path,
            Level = level,
            CreatedAt = DateTime.UtcNow
        };

        _levels[level].Add(info);

        _logger.LogInformation("SST written to {Path} at level {Level}", path, level);

        return path;
    }

    /// <summary>
    ///     Gets compaction candidates (files that need compaction).
    /// </summary>
    public List<SSTFileInfo> GetCompactionCandidates()
    {
        var candidates = new List<SSTFileInfo>();

        // Simple heuristic: compact L0 if too many files
        if (_levels[0].Count >= L0CompactionThreshold)
        {
            candidates.AddRange(_levels[0]);
        }

        return candidates;
    }

    /// <summary>
    ///     Removes SST files after compaction.
    /// </summary>
    public void RemoveSSTs(IEnumerable<SSTFileInfo> ssts)
    {
        foreach (var sst in ssts)
        {
            for (int level = 0; level < _levels.Count; level++)
            {
                _levels[level].RemoveAll(s => s.Path == sst.Path);
            }
        }
    }

    /// <summary>
    ///     Information about an SST file in the level hierarchy.
    /// </summary>
    public class SSTFileInfo
    {
        /// <summary>
        ///     Path to the SST file.
        /// </summary>
        public required string Path { get; init; }

        /// <summary>
        ///     Level in the LSM hierarchy.
        /// </summary>
        public required int Level { get; init; }

        /// <summary>
        ///     When the SST was created.
        /// </summary>
        public required DateTime CreatedAt { get; init; }

        /// <summary>
        ///     Checks if key exists in this SST.
        /// </summary>
        public async ValueTask<bool> ExistsAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
        {
            // Placeholder: in production, use bloom filters
            var value = await GetAsync(key, ct);
            return value.HasValue;
        }

        /// <summary>
        ///     Gets value from this SST.
        /// </summary>
        public async ValueTask<ReadOnlyMemory<byte>?> GetAsync(ReadOnlyMemory<byte> key, CancellationToken ct = default)
        {
            // Placeholder: in production, would read from actual SST file
            await Task.CompletedTask;
            return null;
        }
    }
}
