namespace Gravel.Exceptions;

/// <summary>
///     Gravel-specific ArgumentOutOfRangeException for domain use.
/// </summary>
public class GravelArgumentOutOfRangeException : ArgumentOutOfRangeException
{
    public GravelArgumentOutOfRangeException()
    {
    }

    public GravelArgumentOutOfRangeException(string paramName) : base(paramName)
    {
    }

    public GravelArgumentOutOfRangeException(string paramName, string message) : base(paramName, message)
    {
    }
}
