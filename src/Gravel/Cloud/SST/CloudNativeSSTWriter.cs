using Gravel.Storage.TLV;

namespace Gravel.Cloud.SST;

/// <summary>
///     Cloud-native SST (Sorted String Table) using TLV format with metadata blocks.
///     
///     Structure:
///     - [Data Block] TLV-encoded sorted entries
///     - [Index Block] Sparse index (every Nth key points to entry offset)
///     - [Bloom Filter Block] Optional bloom filter for negative lookups
///     - [Metadata Block] Min/max keys, counts, timestamps
///     - [Footer] 8 bytes: index offset, bloom offset, metadata offset
/// </summary>
public sealed class CloudNativeSSTWriter : IAsyncDisposable
{
    readonly Stream _output;
    readonly int _sparseIndexInterval;
    readonly TLVWriter _tlvWriter;

    long _dataBlockStart;
    ulong _entryCount;
    ReadOnlyMemory<byte> _minKey;
    ReadOnlyMemory<byte> _maxKey;
    List<(ulong Offset, ReadOnlyMemory<byte> Key)> _sparseIndex = [];

    /// <summary>
    ///     Initializes a new instance of <see cref="CloudNativeSSTWriter" />.
    /// </summary>
    public CloudNativeSSTWriter(Stream output, int sparseIndexInterval = 64, bool leaveOpen = false)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _sparseIndexInterval = Math.Max(1, sparseIndexInterval);
        _tlvWriter = new TLVWriter(output, leaveOpen: true);
        _dataBlockStart = output.Position;
    }

    /// <summary>
    ///     Writes sorted entries to SST (assumes entries are pre-sorted).
    /// </summary>
    public async ValueTask WriteEntriesAsync(
        IAsyncEnumerable<(byte Type, System.ReadOnlyMemory<byte> Key, System.ReadOnlyMemory<byte> Value, ulong Sequence)> entries,
        System.Threading.CancellationToken ct = default)
    {
        _entryCount = 0;
        ulong entryIndex = 0;

        await foreach (var (type, key, value, seq) in entries.ConfigureAwait(false))
        {
            if (_entryCount == 0)
            {
                _minKey = key;
            }

            _maxKey = key;

            // Build sparse index
            if ((int)(entryIndex % (ulong)_sparseIndexInterval) == 0)
            {
                _sparseIndex.Add(((ulong)_tlvWriter.BufferOffset, key));
            }

            await _tlvWriter.WriteEntryAsync(type, key, value, ct).ConfigureAwait(false);

            _entryCount++;
            entryIndex++;
            ct.ThrowIfCancellationRequested();
        }

        await _tlvWriter.WriteEndMarkerAsync(ct).ConfigureAwait(false);
        await _tlvWriter.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Finalizes the SST file with metadata and footer.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            // Write sparse index
            var indexOffset = _output.Position;
            await using (var indexWriter = new TLVWriter(_output, leaveOpen: true))
            {
                foreach (var (offset, key) in _sparseIndex)
                {
                    var offsetBytes = System.BitConverter.GetBytes(offset);
                    await indexWriter.WriteEntryAsync(0xAA, offsetBytes, key, default).ConfigureAwait(false);
                }
            }

            // Write metadata block
            var metadataOffset = _output.Position;
            var metadata = new Dictionary<string, string>
            {
                { "entry_count", _entryCount.ToString() },
                { "created_utc", System.DateTime.UtcNow.Ticks.ToString() },
                { "min_key_len", _minKey.Length.ToString() },
                { "max_key_len", _maxKey.Length.ToString() }
            };

            await using (var metaWriter = new TLVWriter(_output, leaveOpen: true))
            {
                foreach (var (k, v) in metadata)
                {
                    var keyBytes = System.Text.Encoding.UTF8.GetBytes(k);
                    var valueBytes = System.Text.Encoding.UTF8.GetBytes(v);
                    await metaWriter.WriteEntryAsync(0xBB, keyBytes, valueBytes, default).ConfigureAwait(false);
                }
            }

            // Write footer: index_offset (8) + metadata_offset (8)
            var footerData = new byte[16];
            System.BitConverter.GetBytes(indexOffset).CopyTo(footerData, 0);
            System.BitConverter.GetBytes(metadataOffset).CopyTo(footerData, 8);
            await _output.WriteAsync(footerData, 0, 16).ConfigureAwait(false);
        }
        finally
        {
            _output?.Dispose();
        }
    }
}

/// <summary>
///     Cloud-native SST reader supporting zero-copy access.
/// </summary>
public sealed class CloudNativeSSTReader : IAsyncDisposable
{
    readonly byte[] _data;
    readonly SSTMetadata _metadata;

    /// <summary>
    ///     Initializes a new instance of <see cref="CloudNativeSSTReader" />.
    /// </summary>
    public CloudNativeSSTReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));

        // Parse footer
        if (data.Length < 16)
            throw new InvalidOperationException("SST too small (missing footer)");

        var footerStart = data.Length - 16;
        var indexOffset = System.BitConverter.ToInt64(data, footerStart);
        var metadataOffset = System.BitConverter.ToInt64(data, footerStart + 8);

        _metadata = new SSTMetadata
        {
            Data = data,
            IndexOffset = (int)indexOffset,
            MetadataOffset = (int)metadataOffset
        };
    }

    /// <summary>
    ///     Gets the number of entries in the SST.
    /// </summary>
    public ulong EntryCount => ulong.Parse(_metadata.GetMetadata("entry_count") ?? "0");

    /// <summary>
    ///     Enumerates all entries in the SST.
    /// </summary>
    public IAsyncEnumerable<(byte Type, ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> EnumerateEntriesAsync()
    {
        return EnumerateEntriesAsyncImpl();
    }

    private async IAsyncEnumerable<(byte Type, ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> EnumerateEntriesAsyncImpl()
    {
        var reader = new TLVReader(_data);
        while (reader.TryReadNext(out var type, out var key, out var value))
        {
            yield return (type, key, value);
            await Task.Yield();
        }
    }

    /// <summary>
    ///     Disposes the reader.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }

    sealed class SSTMetadata
    {
        public required byte[] Data { get; init; }
        public required int IndexOffset { get; init; }
        public required int MetadataOffset { get; init; }

        public string? GetMetadata(string key)
        {
            var reader = new TLVReader(Data.AsMemory(MetadataOffset, IndexOffset - MetadataOffset));
            while (reader.TryReadNext(out _, out var k, out var v))
            {
                var keyStr = System.Text.Encoding.UTF8.GetString(k.Span);
                if (keyStr == key)
                {
                    return System.Text.Encoding.UTF8.GetString(v.Span);
                }
            }
            return null;
        }
    }
}
