using System.Buffers.Binary;
using Gravel.Internals;

namespace Gravel.Abstractions.Storage.Sst;

sealed class DataBlockBuilder
{
    readonly MemoryStream _buf = new();
    readonly int _restartInterval;
    readonly List<int> _restarts = [0];
    int _entrySinceRestart;
    byte[] _prevKey = [];

    public DataBlockBuilder(int restartInterval = 16)
    {
        _restartInterval = Math.Max(1, restartInterval);
    }

    public int CurrentSize => (int)_buf.Length + (_restarts.Count * 4) + 4;

    public void Add(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var shared = 0;
        if (_entrySinceRestart < _restartInterval)
        {
            var maxShared = Math.Min(_prevKey.Length, key.Length);
            while (shared < maxShared && _prevKey[shared] == key[shared]) shared++;
        }

        var unshared = key.Length - shared;

        Span<byte> hdr = stackalloc byte[12];
        var n = 0;
        n += Varint.Write32(hdr[n..], (uint)shared);
        n += Varint.Write32(hdr[n..], (uint)unshared);
        n += Varint.Write32(hdr[n..], (uint)value.Length);
        _buf.Write(hdr[..n]);

        if (unshared > 0) _buf.Write(key.Slice(shared, unshared));
        if (!value.IsEmpty) _buf.Write(value);

        if (++_entrySinceRestart >= _restartInterval)
        {
            _restarts.Add((int)_buf.Length);
            _entrySinceRestart = 0;
            _prevKey = key.ToArray();
        }
        else
        {
            _prevKey = key.ToArray();
        }
    }

    public byte[] Finish()
    {
        foreach (var off in _restarts)
        {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(tmp, off);
            _buf.Write(tmp);
        }

        Span<byte> cnt = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(cnt, _restarts.Count);
        _buf.Write(cnt);
        return _buf.ToArray();
    }

    public void Reset()
    {
        _buf.SetLength(0);
        _restarts.Clear();
        _restarts.Add(0);
        _entrySinceRestart = 0;
        _prevKey = [];
    }
}