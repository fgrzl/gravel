namespace Gravel.Internals;

public static class Crc32C
{
    static readonly uint[] Table = GenerateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            var idx = (byte)(crc ^ b);
            crc = Table[idx] ^ (crc >> 8);
        }

        return ~crc;
    }

    static uint[] GenerateTable()
    {
        const uint poly = 0x1EDC6F41;
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
                c = (c & 1) != 0 ? poly ^ (c >> 1) : c >> 1;
            t[i] = c;
        }

        return t;
    }
}