using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Gravel.Engine;

public sealed class FileSystemEngineTests : EngineTestsBase, IAsyncLifetime
{
    string _temp = string.Empty;

    public Task InitializeAsync()
    {
        _temp = Path.Combine(Path.GetTempPath(), "gravel-test-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);


        return GravelFactory.CreateFileSystemAsync(_temp);
    }

    public async Task DisposeAsync()
    {
        try
        {
            await Engine.DisposeAsync().AsTask();
            Directory.Delete(_temp!, true);
        }
        catch
        {
            // no-op best-effort cleanup
        }
    }
}

public sealed class InMemoryEngineTests : EngineTestsBase, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        return GravelFactory.CreateInMemoryAsync();
    }

    public async Task DisposeAsync()
    {
        await Engine.DisposeAsync().AsTask();
    }
}