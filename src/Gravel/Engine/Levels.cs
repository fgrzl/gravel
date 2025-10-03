namespace Gravel.Engine;

/// <summary>
///     Encapsulates LSM tree level organization and synchronization.
/// </summary>
sealed class Levels(int levelCount)
{
    readonly List<List<SstFile>> _levels = Enumerable.Range(0, levelCount).Select(_ => new List<SstFile>()).ToList();
    readonly object _sync = new();

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

    public void Add(int level, SstFile file)
    {
        lock (_sync)
        {
            _levels[level].Add(file);
        }
    }

    public IReadOnlyList<IReadOnlyList<SstFile>> SnapshotLevels()
    {
        lock (_sync)
        {
            var outer = new List<IReadOnlyList<SstFile>>(_levels.Count);
            foreach (var l in _levels)
                outer.Add(l.ToList().AsReadOnly()); // deep copy + readonly wrapper
            return outer.AsReadOnly();
        }
    }

    public IReadOnlyList<SstFile> SnapshotAll()
    {
        lock (_sync)
        {
            return _levels.SelectMany(l => l).ToList().AsReadOnly();
        }
    }

    public bool MeetsFanIn(int level, int threshold)
    {
        lock (_sync)
        {
            return _levels[level].Count >= threshold;
        }
    }

    public List<SstFile> TakeLevel(int level)
    {
        lock (_sync)
        {
            var list = _levels[level].ToList();
            _levels[level].Clear();
            return list; // caller mutates isolated copy
        }
    }

    /// <summary>
    ///     Atomically remove a set of input files from a level and add an output file to another level.
    ///     Used to install compaction results without creating a visibility gap.
    /// </summary>
    public void ApplyCompaction(int fromLevel, IReadOnlyList<SstFile> inputs, int toLevel, SstFile output)
    {
        lock (_sync)
        {
            // Remove inputs that are still present
            var src = _levels[fromLevel];
            for (var i = src.Count - 1; i >= 0; i--)
                if (inputs.Contains(src[i]))
                    src.RemoveAt(i);

            // Install output
            _levels[toLevel].Add(output);
        }
    }
}