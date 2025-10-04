namespace Gravel.Exceptions;

/// <summary>
///     Root exception type for Gravel domain errors.
/// </summary>
public class GravelException : Exception
{
    public GravelException()
    {
    }

    public GravelException(string message) : base(message)
    {
    }

    public GravelException(string message, Exception inner) : base(message, inner)
    {
    }
}
