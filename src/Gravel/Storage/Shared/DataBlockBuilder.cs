using System.Buffers;
using System.Buffers.Binary;
using Gravel.Internals;

namespace Gravel.Storage.Shared;

/// <summary>
/// Builds SST data blocks using prefix compression with restart points.
/// </summary>
/// <param name="restartInterval">Number of entries between restart points (minimum 1).</param>
public sealed class DataBlockBuilder(int restartInterval = 16)
{
    readonly MemoryStream _buf = new();
    readonly int _restartInterval = Math.Max(1, restartInterval);
    readonly List<int> _restarts = [0];
    int _entrySinceRestart;
    byte[] _prevKey = [];

    /// <summary>
    /// Gets the current serialized size of the block including restart array and count.
    /// </summary>
    public int CurrentSize => (int)_buf.Length + _restarts.Count * 4 + 4;

    /// <summary>
    /// Adds an entry to the block using shared-prefix compression relative to the previous key.
    /// </summary>
    /// <param name="key">The internal key to add (includes sequence/type trailer if applicable).</param>
    /// <param name="value">The associated value bytes.</param>
    public void Add(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var shared = 0;
        // IMPORTANT: the first entry after a restart (or at the beginning) must use shared=0
        // so that a reader can reconstruct keys starting from that restart offset.
        if (_entrySinceRestart > 0)
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

    /// <summary>
    /// Finalizes the block and returns a newly allocated byte array.
    /// </summary>
    /// <returns>The serialized block bytes.</returns>
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

    /// <summary>
    /// Finalizes the block into a pooled buffer to avoid an allocation. Caller must return the buffer.
    /// </summary>
    /// <returns>A <see cref="PooledBuffer"/> containing the serialized block.</returns>
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

    /// <summary>
    /// Resets the builder for reuse by clearing buffers and state.
    /// </summary>
    public void Reset()
    {
        _buf.SetLength(0);
        _restarts.Clear();
        _restarts.Add(0);
        _entrySinceRestart = 0;
        _prevKey = [];
    }

    /// <summary>
    /// Pooled result to avoid allocating the final array. Caller must return the buffer to the pool.
    /// </summary>
    /// <param name="buffer">The rented buffer containing the block bytes.</param>
    /// <param name="length">The number of valid bytes in <see cref="Buffer"/>.</param>
    public struct PooledBuffer(byte[] buffer, int length)
    {
        /// <summary>
        /// Gets the rented buffer containing the serialized block.
        /// </summary>
        public byte[] Buffer { get; } = buffer;
        /// <summary>
        /// Gets the number of valid bytes in <see cref="Buffer"/>.
        /// </summary>
        public int Length { get; } = length;
    }
}
