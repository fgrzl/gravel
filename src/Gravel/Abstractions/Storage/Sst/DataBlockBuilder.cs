using System.Buffers;
using System.Buffers.Binary;
using Gravel.Internals;

namespace Gravel.Abstractions.Storage.Sst;

sealed class DataBlockBuilder(int restartInterval = 16)
{
    readonly MemoryStream _buf = new();
    readonly int _restartInterval = Math.Max(1, restartInterval);
    readonly List<int> _restarts = [0];
    int _entrySinceRestart;
    byte[] _prevKey = [];

    public int CurrentSize => (int)_buf.Length + _restarts.Count * 4 + 4;

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
        n += VarInt.Write32(hdr[n..], (uint)shared);
        n += VarInt.Write32(hdr[n..], (uint)unshared);
        n += VarInt.Write32(hdr[n..], (uint)value.Length);
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
        // fallback convenience: produce a new array (compatible with existing callers)
        var totalLen = (int)_buf.Length + _restarts.Count * 4 + 4;
        var result = new byte[totalLen];
        _buf.Position = 0;
        _buf.Read(result, 0, (int)_buf.Length);
        var offset = (int)_buf.Length;

        Span<byte> tmp = stackalloc byte[4];
        foreach (var off in _restarts)
        {
            BinaryPrimitives.WriteInt32LittleEndian(tmp, off);
            tmp.CopyTo(result.AsSpan(offset, 4));
            offset += 4;
        }

        Span<byte> cnt = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(cnt, _restarts.Count);
        cnt.CopyTo(result.AsSpan(offset, 4));

        return result;
    }

    public PooledBuffer FinishPooled()
    {
        var totalLen = (int)_buf.Length + _restarts.Count * 4 + 4;
        var pooled = ArrayPool<byte>.Shared.Rent(totalLen);
        try
        {
            _buf.Position = 0;
            var read = _buf.Read(pooled, 0, (int)_buf.Length);
            var offset = read;

            Span<byte> tmp = stackalloc byte[4];
            foreach (var off in _restarts)
            {
                BinaryPrimitives.WriteInt32LittleEndian(tmp, off);
                tmp.CopyTo(pooled.AsSpan(offset, 4));
                offset += 4;
            }

            Span<byte> cnt = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(cnt, _restarts.Count);
            cnt.CopyTo(pooled.AsSpan(offset, 4));

            return new PooledBuffer(pooled, totalLen);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(pooled);
            throw;
        }
    }

    public void Reset()
    {
        _buf.SetLength(0);
        _restarts.Clear();
        _restarts.Add(0);
        _entrySinceRestart = 0;
        _prevKey = [];
    }

    // Pooled result to avoid allocating the final array. Caller must return the buffer when done.
    public struct PooledBuffer(byte[] buffer, int length)
    {
        public byte[] Buffer { get; } = buffer;
        public int Length { get; } = length;
    }
}
