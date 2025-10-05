namespace Gravel.Exceptions;

/// <summary>
///     Gravel-specific <see cref="InvalidOperationException"/> so callers can catch either the base exception or this more specific type.
/// </summary>
public class GravelInvalidOperationException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of <see cref="GravelInvalidOperationException"/>.
    /// </summary>
    public GravelInvalidOperationException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of <see cref="GravelInvalidOperationException"/> with a specified error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public GravelInvalidOperationException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of <see cref="GravelInvalidOperationException"/> with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public GravelInvalidOperationException(string message, Exception inner) : base(message, inner)
    {
    }
}
