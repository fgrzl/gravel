namespace Gravel.Storage.TLV;

/// <summary>
///     Zero-copy TLV writer for efficient sequential output.
///     Buffers writes and supports streaming to disk/cloud.
/// </summary>
public sealed class TLVWriter : IAsyncDisposable
{
    readonly int _bufferSize;
    byte[] _buffer;
    int _offset;
    readonly Stream _output;
    readonly bool _leaveOpen;

    /// <summary>
    ///     Initializes a new instance of <see cref="TLVWriter" />.
    /// </summary>
    public TLVWriter(Stream output, int bufferSize = 64 * 1024, bool leaveOpen = false)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _bufferSize = bufferSize;
        _buffer = new byte[bufferSize];
        _offset = 0;
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    ///     Gets the current offset in the write buffer.
    /// </summary>
    public int BufferOffset => _offset;

    /// <summary>
    ///     Gets bytes written to the underlying stream.
    /// </summary>
    public long TotalBytesWritten { get; private set; }

    /// <summary>
    ///     Writes a TLV entry. Flushes buffer if needed.
    /// </summary>
    public async ValueTask WriteEntryAsync(
        byte type,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> value,
        CancellationToken ct = default)
    {
        var needed = TLVFormat.CalculateEntrySize(key.Length, value.Length);

        // Flush if entry doesn't fit
        if (_offset + needed > _buffer.Length)
        {
            await FlushAsync(ct).ConfigureAwait(false);
        }

        // Write entry
        var written = TLVFormat.WriteEntry(_buffer, _offset, type, key.Span, value.Span);
        if (written < 0)
            throw new InvalidOperationException("Failed to write TLV entry (buffer too small)");

        _offset += written;
    }

    /// <summary>
    ///     Writes multiple entries efficiently.
    /// </summary>
    public async ValueTask WriteEntriesAsync(
        IAsyncEnumerable<(byte Type, ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries,
        CancellationToken ct = default)
    {
        await foreach (var (type, key, value) in entries.ConfigureAwait(false))
        {
            await WriteEntryAsync(type, key, value, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Writes end-of-block marker.
    /// </summary>
    public async ValueTask WriteEndMarkerAsync(CancellationToken ct = default)
    {
        if (_offset + 1 > _buffer.Length)
        {
            await FlushAsync(ct).ConfigureAwait(false);
        }

        var written = TLVFormat.WriteEndMarker(_buffer, _offset);
        if (written < 0)
            throw new InvalidOperationException("Failed to write end marker");

        _offset += written;
    }

    /// <summary>
    ///     Flushes the buffer to the underlying stream.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken ct = default)
    {
        if (_offset > 0)
        {
            await _output.WriteAsync(_buffer.AsMemory(0, _offset), ct).ConfigureAwait(false);
            TotalBytesWritten += _offset;
            _offset = 0;
        }
    }

    /// <summary>
    ///     Finalizes writing and closes the stream.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await WriteEndMarkerAsync().ConfigureAwait(false);
            await FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            if (!_leaveOpen)
            {
                _output?.Dispose();
            }
        }
    }
}
