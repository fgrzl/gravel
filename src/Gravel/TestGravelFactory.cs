using Gravel.Abstractions;

namespace Gravel;

class ByteArrayComparer : IComparer<byte[]>
{
    public int Compare(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var min = Math.Min(x.Length, y.Length);
        for (var i = 0; i < min; i++)
        {
            int a = x[i];
            int b = y[i];
            if (a != b) return a - b;
        }

        return x.Length - y.Length;
    }
}

class TestGravelFactory : IGravelFactory
{
    public IGravelDb Open(string path)
    {
        return new TestGravelDb();
    }
}

class TestGravelDb : IGravelDb
{
    readonly ReaderWriterLockSlim _lock = new();
    readonly SortedDictionary<byte[], byte[]> _store = new(new ByteArrayComparer());
    bool _disposed;

    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        _lock.EnterWriteLock();
        try
        {
            var k = key.ToArray();
            var v = value.ToArray();
            _store[k] = v;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool TryGet(ReadOnlySpan<byte> key, out ReadOnlyMemory<byte> value)
    {
        _lock.EnterReadLock();
        try
        {
            var k = key.ToArray();
            if (_store.TryGetValue(k, out var v))
            {
                value = v;
                return true;
            }

            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Delete(ReadOnlySpan<byte> key)
    {
        _lock.EnterWriteLock();
        try
        {
            var k = key.ToArray();
            _store.Remove(k);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IGravelIterator NewIterator()
    {
        _lock.EnterReadLock();
        try
        {
            // Snapshot keys for iteration
            var keys = _store.Keys.ToArray();
            return new GravelIterator(keys, _store, _lock);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}

class GravelIterator : IGravelIterator
{
    readonly byte[][] _keys;
    readonly ReaderWriterLockSlim _lock;
    readonly SortedDictionary<byte[], byte[]> _store;
    int _index = -1;

    public GravelIterator(byte[][] keys, SortedDictionary<byte[], byte[]> store, ReaderWriterLockSlim @lock)
    {
        _keys = keys;
        _store = store;
        _lock = @lock;
        // keep a read lock for iterator lifetime to protect snapshot consistency
        _lock.EnterReadLock();
    }

    public bool SeekGE(ReadOnlySpan<byte> target)
    {
        var t = target.ToArray();
        // binary search on keys
        int lo = 0, hi = _keys.Length - 1, pos = _keys.Length;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var cmp = Compare(_keys[mid], t);
            if (cmp >= 0)
            {
                pos = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        _index = pos;
        return Valid;
    }

    public bool Next()
    {
        if (_index < 0)
            _index = 0;
        else
            _index++;
        return Valid;
    }

    public bool Valid => _index >= 0 && _index < _keys.Length;

    public ReadOnlyMemory<byte> Key
    {
        get
        {
            if (!Valid) return ReadOnlyMemory<byte>.Empty;
            return _keys[_index];
        }
    }

    public ReadOnlyMemory<byte> Value
    {
        get
        {
            if (!Valid) return ReadOnlyMemory<byte>.Empty;
            if (_store.TryGetValue(_keys[_index], out var v))
                return v;
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    public void Dispose()
    {
        try
        {
        }
        finally
        {
            if (_lock.IsReadLockHeld)
                _lock.ExitReadLock();
        }
    }

    static int Compare(byte[] a, byte[] b)
    {
        var min = Math.Min(a.Length, b.Length);
        for (var i = 0; i < min; i++)
        {
            int x = a[i];
            int y = b[i];
            if (x != y) return x - y;
        }

        return a.Length - b.Length;
    }
}

class TestMemTable : IMemTable
{
    readonly Dictionary<string, string> _store = new();

    public void Insert(string key, string value)
    {
        _store[key] = value;
    }

    public string? Get(string key)
    {
        return _store.TryGetValue(key, out var v) ? v : null;
    }

    public void Delete(string key)
    {
        _store.Remove(key);
    }

    public void Clear()
    {
        _store.Clear();
    }
}