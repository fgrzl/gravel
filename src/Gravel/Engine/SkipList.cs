namespace Gravel.Engine;

/// <summary>
///     Generic skip list mapping ordered keys to values.
///     Provides efficient insert, update, delete, and scan operations.
/// </summary>
public sealed class SkipList<TKey, TValue>(IComparer<TKey>? comparer = null)
{
    /// <summary>
    ///     Maximum level for skip list nodes.
    /// </summary>
    const int MaxLevel = 16;

    /// <summary>
    ///     Comparer used for key ordering.
    /// </summary>
    readonly IComparer<TKey> _comparer = comparer ?? Comparer<TKey>.Default;

    /// <summary>
    ///     Dummy head node for skip list.
    /// </summary>
    readonly Node _head = new(default!, default!, MaxLevel);

    /// <summary>
    ///     Random number generator for level assignment.
    /// </summary>
    readonly Random _rand = new();

    int _level = 1;

    /// <summary>
    ///     Gets the number of entries in the skip list.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    ///     Tries to get the value for a given key.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The value if found.</param>
    /// <returns>True if found, otherwise false.</returns>
    public bool TryGet(TKey key, out TValue? value)
    {
        var x = _head;
        for (var i = _level - 1; i >= 0; i--)
            while (x.Forward[i] != null && _comparer.Compare(x.Forward[i]!.Key, key) < 0)
                x = x.Forward[i]!;

        x = x.Forward[0];
        if (x != null && _comparer.Compare(x.Key, key) == 0)
        {
            value = x.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    ///     Inserts or updates a key-value pair in the skip list.
    /// </summary>
    /// <param name="key">The key to insert or update.</param>
    /// <param name="value">The value to associate.</param>
    public void InsertOrUpdate(TKey key, TValue value)
    {
        var update = new Node?[MaxLevel];
        var x = _head;

        for (var i = _level - 1; i >= 0; i--)
        {
            while (x.Forward[i] != null && _comparer.Compare(x.Forward[i]!.Key, key) < 0)
                x = x.Forward[i]!;
            update[i] = x;
        }

        x = x.Forward[0];
        if (x != null && _comparer.Compare(x.Key, key) == 0)
        {
            // Update existing
            x.Value = value;
            return;
        }

        var lvl = RandomLevel();
        if (lvl > _level)
        {
            for (var i = _level; i < lvl; i++)
                update[i] = _head;
            _level = lvl;
        }

        var newNode = new Node(key, value, lvl);
        for (var i = 0; i < lvl; i++)
        {
            newNode.Forward[i] = update[i]!.Forward[i];
            update[i]!.Forward[i] = newNode;
        }

        Count++;
    }

    /// <summary>
    ///     Deletes a key from the skip list.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>True if the key was deleted, otherwise false.</returns>
    public bool Delete(TKey key)
    {
        var update = new Node?[MaxLevel];
        var x = _head;

        for (var i = _level - 1; i >= 0; i--)
        {
            while (x.Forward[i] != null && _comparer.Compare(x.Forward[i]!.Key, key) < 0)
                x = x.Forward[i]!;
            update[i] = x;
        }

        x = x.Forward[0];
        if (x == null || _comparer.Compare(x.Key, key) != 0)
            return false;

        for (var i = 0; i < _level; i++)
            if (update[i]!.Forward[i] == x)
                update[i]!.Forward[i] = x.Forward[i];

        while (_level > 1 && _head.Forward[_level - 1] == null)
            _level--;

        Count--;
        return true;
    }

    /// <summary>
    ///     Scans the skip list for entries in the specified range.
    /// </summary>
    /// <param name="start">Optional start key (inclusive).</param>
    /// <param name="end">Optional end key (exclusive).</param>
    /// <param name="hasStart">True if start key is specified.</param>
    /// <param name="hasEnd">True if end key is specified.</param>
    /// <returns>An enumerable of key/value pairs.</returns>
    public IEnumerable<(TKey Key, TValue Value)> Scan(
        TKey? start = default,
        TKey? end = default,
        bool hasStart = false,
        bool hasEnd = false)
    {
        var x = _head.Forward[0];

        if (hasStart)
            // Seek to start
            for (var i = _level - 1; i >= 0; i--)
                while (x != null && _comparer.Compare(x.Key, start!) < 0)
                    x = x.Forward[0];

        while (x != null)
        {
            if (hasEnd && _comparer.Compare(x.Key, end!) >= 0)
                yield break;

            yield return (x.Key, x.Value);
            x = x.Forward[0];
        }
    }

    /// <summary>
    ///     Generates a random level for skip list insertion.
    /// </summary>
    /// <returns>The random level.</returns>
    int RandomLevel()
    {
        var lvl = 1;
        while (lvl < MaxLevel && (_rand.Next() & 1) == 1)
            lvl++;
        return lvl;
    }

    /// <summary>
    ///     Skip list node for key/value pairs.
    /// </summary>
    sealed class Node(TKey key, TValue value, int level)
    {
        /// <summary>
        ///     Forward pointers for each level.
        /// </summary>
        public readonly Node?[] Forward = new Node[level];

        /// <summary>
        ///     The key stored in this node.
        /// </summary>
        public readonly TKey Key = key;

        /// <summary>
        ///     The value stored in this node.
        /// </summary>
        public TValue Value = value;
    }
}
