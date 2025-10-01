namespace Gravel.Internals;

/// <summary>
///     Generates monotonic 64-bit sequence values composed of a 32-bit unix timestamp (seconds)
///     in the high 32 bits and a 32-bit counter in the low 32 bits. This allows sequences to
///     advance with wall-clock time while producing additional unique values within the same
///     second by incrementing the low 32-bit counter.
/// </summary>
public static class Sequence
{
    /// <summary>
    ///     Given the current sequence value, produce the next sequence value that is strictly
    ///     greater than the provided value. High 32 bits encode Unix time (seconds UTC). Low 32
    ///     bits encode a per-second counter (unsigned).
    /// </summary>
    /// <remarks>
    ///     Behavior notes:
    ///     - Uses <see cref="Timestamp.GetTimestamp" /> (milliseconds) as the authoritative time source.
    ///     - If wall-clock time advances to a new second, the low counter is reset to 0.
    ///     - If the provided current value is ahead of the clock (clock moved backwards), the
    ///     implementation will increment the low counter while keeping the high-second component
    ///     equal to the current value's seconds so that sequences remain strictly increasing.
    ///     - If the low counter wraps (reaches <see cref="uint.MaxValue" />), the method will
    ///     wait (sleep) until the timestamp seconds advances and then resume with counter 0.
    ///     This can block for up to ~1 second in the worst case; callers on latency-sensitive
    ///     paths should avoid relying on extremely high per-second throughput on a single process.
    /// </remarks>
    /// <param name="current">The current sequence value (maybe 0 to start fresh).</param>
    /// <returns>A sequence value greater than <paramref name="current" />.</returns>
    public static ulong GetNext(ulong current)
    {
        var currSec = (uint)(current >> 32);
        var currCounter = (uint)current;

        // Use Timestamp as authoritative time source (milliseconds)
        var nowMillis = Timestamp.GetTimestamp();
        var nowSec = (uint)(nowMillis / 1000);

        // If time has moved forward, start counter at 0 for the new second.
        if (nowSec > currSec) return Compose(nowSec, 0);

        // same second (or clock moved backwards). Increment the low counter.
        // If counter wraps (unlikely), wait for clock to advance by at least one second.
        if (currCounter == uint.MaxValue)
        {
            // Wait until timestamp seconds advances to avoid producing duplicate sequences.
            while ((uint)(Timestamp.GetTimestamp() / 1000) <= currSec) Thread.Sleep(1);

            var newSec = (uint)(Timestamp.GetTimestamp() / 1000);
            return Compose(newSec, 0);
        }

        return Compose(currSec, currCounter + 1);
    }

    static ulong Compose(uint seconds, uint counter)
    {
        return ((ulong)seconds << 32) | counter;
    }
}