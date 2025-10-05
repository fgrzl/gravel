namespace Gravel.Internals;

/// <summary>
///     Provides static methods for computing CRC32C checksums.
///     Uses a precomputed table for fast calculation.
/// </summary>
public static class Crc32C
{
    /// <summary>
    ///     Precomputed CRC32C table for fast lookup.
    /// </summary>
    static readonly uint[] Table = GenerateTable();

    /// <summary>
    ///     Computes the CRC32C checksum for the given data.
    /// </summary>
    /// <param name="data">The input data to compute the checksum for.</param>
    /// <returns>The CRC32C checksum as a 32-bit unsigned integer.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            var idx = (byte)(crc ^ b);
            crc = Table[idx] ^ crc >> 8;
        }

        return ~crc;
    }

    /// <summary>
    ///     Generates the CRC32C lookup table.
    /// </summary>
    /// <returns>The generated table as an array of 256 unsigned integers.</returns>
    static uint[] GenerateTable()
    {
        const uint poly = 0x1EDC6F41;
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
                c = (c & 1) != 0 ? poly ^ c >> 1 : c >> 1;
            t[i] = c;
        }

        return t;
    }
}
