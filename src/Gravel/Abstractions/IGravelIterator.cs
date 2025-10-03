namespace Gravel.Abstractions;

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