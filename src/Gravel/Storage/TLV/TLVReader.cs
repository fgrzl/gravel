namespace Gravel.Storage.TLV;

/// <summary>
///     Zero-copy TLV reader for efficient sequential access.
///     Used for WAL replay and SST scanning without intermediate allocations.
/// </summary>
public sealed class TLVReader
{
    readonly ReadOnlyMemory<byte> _buffer;
    int _offset;

    /// <summary>
    ///     Initializes a new instance of <see cref="TLVReader" />.
    /// </summary>
    public TLVReader(ReadOnlyMemory<byte> buffer)
    {
        _buffer = buffer;
        _offset = 0;
    }

    /// <summary>
    ///     Gets the current offset in the buffer.
    /// </summary>
    public int Offset => _offset;

    /// <summary>
    ///     Gets whether we've reached the end of the buffer.
    /// </summary>
    public bool IsAtEnd => _offset >= _buffer.Length;

    /// <summary>
    ///     Tries to read the next TLV entry.
    /// </summary>
    public bool TryReadNext(
        out byte type,
        out ReadOnlyMemory<byte> key,
        out ReadOnlyMemory<byte> value)
    {
        type = 0;
        key = default;
        value = default;

        if (!TLVFormat.TryReadEntry(_buffer.Span, _offset, out type, out var keySpan, out var valueSpan, out var bytesRead))
            return false;

        if (type == TLVFormat.TypeEnd)
        {
            _offset += bytesRead;
            return false;
        }

        // Create slices pointing into the same buffer (zero-copy)
        var keyOffset = _offset + TLVFormat.MinEntrySize;
        var valueOffset = keyOffset + keySpan.Length;
        key = _buffer.Slice(keyOffset, keySpan.Length);
        value = _buffer.Slice(valueOffset, valueSpan.Length);
        _offset += bytesRead;

        return true;
    }

    /// <summary>
    ///     Resets the reader to the beginning.
    /// </summary>
    public void Reset()
    {
        _offset = 0;
    }

    /// <summary>
    ///     Seeks to a specific offset.
    /// </summary>
    public void Seek(int offset)
    {
        _offset = Math.Max(0, Math.Min(offset, _buffer.Length));
    }

    /// <summary>
    ///     Enumerates all entries in the buffer.
    /// </summary>
    public IEnumerable<(byte Type, ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> EnumerateAll()
    {
        Reset();
        while (TryReadNext(out var type, out var key, out var value))
        {
            yield return (type, key, value);
        }
    }
}
