namespace Gravel.Engine;

/// <summary>
///     Encapsulates LSM tree level organization and synchronization.
///     Provides thread-safe operations for managing SST files across multiple levels.
/// </summary>
public sealed class Levels(int levelCount)
{
    /// <summary>
    ///     Internal storage for SST files in each level.
    /// </summary>
    readonly List<List<SstFile>> _levels = Enumerable.Range(0, levelCount).Select(_ => new List<SstFile>()).ToList();

    /// <summary>
    ///     Synchronization object for thread safety.
    /// </summary>
    readonly object _sync = new();

    /// <summary>
    ///     Gets the number of levels in the LSM tree.
    /// </summary>
    public int LevelCount
    {
        get
        {
            lock (_sync)
            {
                return _levels.Count;
            }
        }
    }

    /// <summary>
    ///     Adds an SST file to the specified level.
    /// </summary>
    /// <param name="level">The level to add the file to.</param>
    /// <param name="file">The SST file to add.</param>
    public void Add(int level, SstFile file)
    {
        lock (_sync)
        {
            _levels[level].Add(file);
        }
    }

    /// <summary>
    ///     Returns a deep, read-only snapshot of all levels and their SST files.
    /// </summary>
    /// <returns>A read-only list of read-only lists of SST files.</returns>
    public IReadOnlyList<IReadOnlyList<SstFile>> SnapshotLevels()
    {
        lock (_sync)
        {
            var outer = new List<IReadOnlyList<SstFile>>(_levels.Count);
            foreach (var l in _levels)
                outer.Add(l.ToList().AsReadOnly());
            return outer.AsReadOnly();
        }
    }

    /// <summary>
    ///     Returns a read-only snapshot of all SST files across all levels.
    /// </summary>
    /// <returns>A read-only list of SST files.</returns>
    public IReadOnlyList<SstFile> SnapshotAll()
    {
        lock (_sync)
        {
            return _levels.SelectMany(l => l).ToList().AsReadOnly();
        }
    }

    /// <summary>
    ///     Checks if the specified level meets the fan-in threshold for compaction.
    /// </summary>
    /// <param name="level">The level to check.</param>
    /// <param name="threshold">The fan-in threshold.</param>
    /// <returns>True if the level meets the threshold; otherwise, false.</returns>
    public bool MeetsFanIn(int level, int threshold)
    {
        lock (_sync)
        {
            return _levels[level].Count >= threshold;
        }
    }

    /// <summary>
    ///     Removes and returns all SST files from the specified level.
    /// </summary>
    /// <param name="level">The level to take files from.</param>
    /// <returns>A list of SST files that were removed.</returns>
    public List<SstFile> TakeLevel(int level)
    {
        lock (_sync)
        {
            var list = _levels[level].ToList();
            _levels[level].Clear();
            return list;
        }
    }

    /// <summary>
    ///     Atomically removes a set of input files from a level and adds an output file to another level.
    ///     Used to install compaction results without creating a visibility gap.
    /// </summary>
    /// <param name="fromLevel">The level to remove input files from.</param>
    /// <param name="inputs">The input files to remove.</param>
    /// <param name="toLevel">The level to add the output file to.</param>
    /// <param name="output">The output SST file to add.</param>
    public void ApplyCompaction(int fromLevel, IReadOnlyList<SstFile> inputs, int toLevel, SstFile output)
    {
        lock (_sync)
        {
            var src = _levels[fromLevel];
            for (var i = src.Count - 1; i >= 0; i--)
                if (inputs.Contains(src[i]))
                    src.RemoveAt(i);
            _levels[toLevel].Add(output);
        }
    }
}
