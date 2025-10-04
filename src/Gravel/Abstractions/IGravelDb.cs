namespace Gravel.Abstractions;

public interface IGravelDb : IDisposable
{
    void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
    bool TryGet(ReadOnlySpan<byte> key, out ReadOnlyMemory<byte> value);
    void Delete(ReadOnlySpan<byte> key);

    // Iteration in sorted order
    IGravelIterator NewIterator();
}
