namespace Gravel.Exceptions;

/// <summary>
///     Gravel-specific InvalidOperationException so callers can catch InvalidOperationException or this more specific
///     type.
/// </summary>
public class GravelInvalidOperationException : InvalidOperationException
{
    public GravelInvalidOperationException()
    {
    }

    public GravelInvalidOperationException(string message) : base(message)
    {
    }

    public GravelInvalidOperationException(string message, Exception inner) : base(message, inner)
    {
    }
}