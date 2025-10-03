namespace Gravel;

public interface IGravelFactory
{
    IGravelDb Open(string path);
}

public interface IGravelDb : IDisposable
{
    void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
    bool TryGet(ReadOnlySpan<byte> key, out ReadOnlyMemory<byte> value);
    void Delete(ReadOnlySpan<byte> key);

    // Iteration in sorted order
    IGravelIterator NewIterator();
}

public interface IGravelIterator : IDisposable
{
    /// <summary>
    ///     Returns true if the iterator is positioned on a valid entry.
    /// </summary>
    bool Valid { get; }

    ReadOnlyMemory<byte> Key { get; }
    ReadOnlyMemory<byte> Value { get; }

    /// <summary>
    ///     Position the iterator at the first entry >= target key.
    ///     Returns true if such an entry exists, false if past end.
    /// </summary>
    bool SeekGE(ReadOnlySpan<byte> target);

    /// <summary>
    ///     Advance to the next entry.
    ///     Returns true if valid, false if past end.
    /// </summary>
    bool Next();
}

public interface IMemTable
{
    void Insert(string key, string value);
    string? Get(string key);
    void Delete(string key);
    void Clear();
}