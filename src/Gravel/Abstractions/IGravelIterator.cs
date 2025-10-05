namespace Gravel.Abstractions;

/// <summary>
///     Iterator interface for traversing key-value entries in Gravel storage.
/// </summary>
public interface IGravelIterator : IDisposable
{
    /// <summary>
    ///     Returns true if the iterator is positioned on a valid entry.
    /// </summary>
    bool Valid { get; }

    /// <summary>
    ///     Gets the key at the current iterator position.
    /// </summary>
    ReadOnlyMemory<byte> Key { get; }

    /// <summary>
    ///     Gets the value at the current iterator position.
    /// </summary>
    ReadOnlyMemory<byte> Value { get; }

    /// <summary>
    ///     Positions the iterator at the first entry greater than or equal to the target key.
    ///     Returns true if such an entry exists, false if past end.
    /// </summary>
    /// <param name="target">The target key to seek to.</param>
    /// <returns>True if positioned on a valid entry, false if past end.</returns>
    bool SeekGe(ReadOnlySpan<byte> target);

    /// <summary>
    ///     Advances the iterator to the next entry.
    ///     Returns true if valid, false if past end.
    /// </summary>
    /// <returns>True if positioned on a valid entry, false if past end.</returns>
    bool Next();
}
