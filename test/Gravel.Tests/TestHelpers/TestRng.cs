using System;

namespace Gravel.TestHelpers;

/// <summary>
///     Shared deterministic RNG helper for tests.
///     Use TestRng.Create(seed) to get a seeded Random instance.
/// </summary>
public static class TestRng
{
    public static Random Create(int seed = 42)
    {
        return new Random(seed);
    }

    public static void NextBytes(int seed, byte[] buffer)
    {
        Create(seed).NextBytes(buffer);
    }
}
