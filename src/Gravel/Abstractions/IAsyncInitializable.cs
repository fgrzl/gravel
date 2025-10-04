using System.Threading;
using System.Threading.Tasks;

namespace Gravel.Abstractions;

public interface IAsyncInitializable
{
    ValueTask InitializeAsync(CancellationToken ct = default);
}
