namespace Gravel.Storage.TLV;

/// <summary>
///     Type-Length-Value encoded format for efficient, zero-copy serialization.
///     Supports streaming and cloud-native storage patterns.
///     
///     Format (little-endian):
///     - Type (1 byte): Entry kind (put, delete, range delete, etc.)
///     - KeyLen (4 bytes): Key length
///     - ValueLen (4 bytes): Value length (0 for deletes)
///     - Key (variable): Raw key bytes
///     - Value (variable): Raw value bytes
///     
///     Total overhead: 9 bytes per entry
/// </summary>
public static class TLVFormat
{
    /// <summary>
    ///     Type marker for put operations.
    /// </summary>
    public const byte TypePut = 0x01;

    /// <summary>
    ///     Type marker for delete operations.
    /// </summary>
    public const byte TypeDelete = 0x02;

    /// <summary>
    ///     Type marker for range delete operations.
    /// </summary>
    public const byte TypeDeleteRange = 0x03;

    /// <summary>
    ///     Marker for end-of-block (used in streaming contexts).
    /// </summary>
    public const byte TypeEnd = 0xFF;

    /// <summary>
    ///     Minimum entry size: 1 byte type + 4 bytes key len + 4 bytes value len.
    /// </summary>
    public const int MinEntrySize = 9;

    /// <summary>
    ///     Reads a TLV entry from a memory buffer without copying.
    /// </summary>
    /// <param name="buffer">The buffer to read from.</param>
    /// <param name="offset">Current offset in buffer.</param>
    /// <param name="type">Output: entry type.</param>
    /// <param name="key">Output: key slice (points into buffer).</param>
    /// <param name="value">Output: value slice (points into buffer).</param>
    /// <param name="bytesRead">Output: total bytes consumed.</param>
    /// <returns>True if read successfully, false if buffer too small or end marker.</returns>
    public static bool TryReadEntry(
        ReadOnlySpan<byte> buffer,
        int offset,
        out byte type,
        out ReadOnlySpan<byte> key,
        out ReadOnlySpan<byte> value,
        out int bytesRead)
    {
        type = 0;
        key = default;
        value = default;
        bytesRead = 0;

        if (offset + MinEntrySize > buffer.Length)
            return false;

        var slice = buffer[offset..];
        type = slice[0];

        if (type == TypeEnd)
        {
            bytesRead = 1;
            return true;
        }

        var keyLen = BitConverter.ToInt32(slice[1..5]);
        var valueLen = BitConverter.ToInt32(slice[5..9]);

        if (keyLen < 0 || valueLen < 0)
            return false;

        var totalLen = MinEntrySize + keyLen + valueLen;
        if (offset + totalLen > buffer.Length)
            return false;

        key = slice[MinEntrySize..(MinEntrySize + keyLen)];
        value = slice[(MinEntrySize + keyLen)..(MinEntrySize + keyLen + valueLen)];
        bytesRead = totalLen;

        return true;
    }

    /// <summary>
    ///     Writes a TLV entry to a buffer without copying.
    /// </summary>
    /// <param name="buffer">The buffer to write to.</param>
    /// <param name="offset">Offset where to write.</param>
    /// <param name="type">Entry type marker.</param>
    /// <param name="key">Key data.</param>
    /// <param name="value">Value data.</param>
    /// <returns>Number of bytes written, or -1 if buffer too small.</returns>
    public static int WriteEntry(
        Span<byte> buffer,
        int offset,
        byte type,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value)
    {
        var totalLen = MinEntrySize + key.Length + value.Length;
        if (offset + totalLen > buffer.Length)
            return -1;

        buffer[offset] = type;
        BitConverter.GetBytes(key.Length).CopyTo(buffer[(offset + 1)..(offset + 5)]);
        BitConverter.GetBytes(value.Length).CopyTo(buffer[(offset + 5)..(offset + 9)]);
        key.CopyTo(buffer[(offset + MinEntrySize)..(offset + MinEntrySize + key.Length)]);
        value.CopyTo(buffer[(offset + MinEntrySize + key.Length)..(offset + totalLen)]);

        return totalLen;
    }

    /// <summary>
    ///     Calculates size needed for an entry without writing.
    /// </summary>
    public static int CalculateEntrySize(int keyLen, int valueLen)
        => MinEntrySize + keyLen + valueLen;

    /// <summary>
    ///     Writes end-of-block marker.
    /// </summary>
    public static int WriteEndMarker(Span<byte> buffer, int offset)
    {
        if (offset + 1 > buffer.Length)
            return -1;
        buffer[offset] = TypeEnd;
        return 1;
    }
}
