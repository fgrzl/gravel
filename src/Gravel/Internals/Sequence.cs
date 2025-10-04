namespace Gravel.Internals;

public static class Sequence
{
    // Returns a 64-bit value where high 32 bits are seconds since Unix epoch,
    // low 32 bits are a counter that increments within the same second.
    public static ulong GetNext(ulong current)
    {
        var high = (uint)(current >> 32);
        var low = (uint)current;

        var nowMillis = Timestamp.GetTimestamp();
        var nowSec = (uint)(nowMillis / 1000);

        if (high < nowSec)
            // Clock has advanced beyond current high-second; start counter at 0.
            return (ulong)nowSec << 32 | 0u;

        if (high == nowSec)
        {
            if (low == uint.MaxValue)
                // Counter will wrap; wait until the second advances.
                // Poll until Timestamp advances to next second.
                while (true)
                {
                    Thread.Sleep(1);
                    nowMillis = Timestamp.GetTimestamp();
                    var nextSec = (uint)(nowMillis / 1000);
                    if (nextSec > high)
                        return (ulong)nextSec << 32 | 0u;
                }

            return (ulong)high << 32 | low + 1;
        }

        // high > nowSec (current is in the future relative to system clock):
        // increment counter to preserve monotonicity.
        return (ulong)high << 32 | low + 1;
    }
}
