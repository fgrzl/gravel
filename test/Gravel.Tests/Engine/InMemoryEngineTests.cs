using System.Threading.Tasks;
using Xunit;

namespace Gravel.Engine;

public sealed class InMemoryEngineTests : EngineTestsBase, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        Engine = await GravelFactory.CreateInMemoryAsync();
    }

    public async Task DisposeAsync()
    {
        await Engine.DisposeAsync().AsTask();
    }
}