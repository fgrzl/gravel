namespace Gravel.Abstractions;

public interface IAsyncInitializable
{
    ValueTask InitializeAsync(CancellationToken ct = default);
}
