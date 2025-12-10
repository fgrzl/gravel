namespace Gravel.Engine;

/// <summary>
///     Generates monotonically increasing sequence numbers for database entries.
///     Used to track operation order and ensure ACID properties.
/// </summary>
public sealed class SequenceGenerator
{
    ulong _currentSequence;
    readonly object _lock = new();

    /// <summary>
    ///     Initializes a new <see cref="SequenceGenerator" />.
    /// </summary>
    public SequenceGenerator(ulong startSequence = 1UL)
    {
        _currentSequence = startSequence;
    }

    /// <summary>
    ///     Gets the next sequence number.
    /// </summary>
    public ulong Next()
    {
        lock (_lock)
        {
            return _currentSequence++;
        }
    }

    /// <summary>
    ///     Gets the current sequence number without incrementing.
    /// </summary>
    public ulong Current
    {
        get
        {
            lock (_lock)
            {
                return _currentSequence;
            }
        }
    }

    /// <summary>
    ///     Sets the sequence number (for recovery).
    /// </summary>
    public void SetCurrent(ulong value)
    {
        lock (_lock)
        {
            if (value >= _currentSequence)
                _currentSequence = value + 1;
        }
    }
}
