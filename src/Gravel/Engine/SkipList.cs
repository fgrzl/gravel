namespace Gravel.Engine;

/// <summary>
///     Generic skip list mapping ordered keys to values.
/// </summary>
public sealed class SkipList<TKey, TValue>(IComparer<TKey>? comparer = null)
{
    const int MaxLevel = 16;
    readonly IComparer<TKey> _comparer = comparer ?? Comparer<TKey>.Default;
    readonly Node _head = new(default!, default!, MaxLevel); // dummy head
    readonly Random _rand = new();

    int _level = 1;

    public int Count { get; private set; }

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

    int RandomLevel()
    {
        var lvl = 1;
        while (lvl < MaxLevel && (_rand.Next() & 1) == 1)
            lvl++;
        return lvl;
    }

    sealed class Node(TKey key, TValue value, int level)
    {
        public readonly Node?[] Forward = new Node[level];
        public readonly TKey Key = key;
        public TValue Value = value;
    }
}