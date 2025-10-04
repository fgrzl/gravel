using Gravel.Internals;

namespace Gravel.Storage.InMemory.Sst;

public sealed class InMemorySst
{
    readonly Dictionary<string, (int Index, byte[] Key, byte[] Value)> _fast;
    public readonly (byte[] Key, byte[] Value)[] Entries;

    public InMemorySst(IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries)
    {
        Entries =
        [
            .. entries.Select(e => (e.Key.ToArray(), e.Value.ToArray()))
                .OrderBy(e => e.Item1, ByteArrayComparer.Instance)
        ];
        _fast = new Dictionary<string, (int, byte[], byte[])>(Entries.Length);
        for (var i = 0; i < Entries.Length; i++)
            _fast[Convert.ToBase64String(Entries[i].Key)] = (i, Entries[i].Key, Entries[i].Value);
    }

    public bool TryGet(ReadOnlySpan<byte> key, out byte[]? value)
    {
        if (_fast.TryGetValue(Convert.ToBase64String(key), out var v))
        {
            value = v.Value;
            return true;
        }

        value = null;
        return false;
    }

    sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            return ByteComparer.Compare(x, y);
        }
    }
}
