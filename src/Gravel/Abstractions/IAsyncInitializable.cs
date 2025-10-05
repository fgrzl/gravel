namespace Gravel.Abstractions;

/// <summary>
/// Provides an interface for types that require asynchronous initialization.
/// </summary>
public interface IAsyncInitializable
{
    /// <summary>
    /// Asynchronously initializes the type.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the initialization operation.</returns>
    ValueTask InitializeAsync(CancellationToken ct = default);
}
