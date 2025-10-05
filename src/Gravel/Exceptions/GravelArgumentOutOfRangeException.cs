namespace Gravel.Exceptions;

/// <summary>
///     Gravel-specific <see cref="ArgumentOutOfRangeException" /> for domain use.
/// </summary>
public class GravelArgumentOutOfRangeException : ArgumentOutOfRangeException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="GravelArgumentOutOfRangeException" /> class.
    /// </summary>
    public GravelArgumentOutOfRangeException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="GravelArgumentOutOfRangeException" /> class with the name of the
    ///     parameter that causes this exception.
    /// </summary>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    public GravelArgumentOutOfRangeException(string paramName) : base(paramName)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="GravelArgumentOutOfRangeException" /> class with the parameter name
    ///     and a specified error message.
    /// </summary>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    /// <param name="message">The message that describes the error.</param>
    public GravelArgumentOutOfRangeException(string paramName, string message) : base(paramName, message)
    {
    }
}
