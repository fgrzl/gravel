using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Gravel.Engine;

public sealed class FileSystemEngineTests : EngineTestsBase, IAsyncLifetime
{
    string _temp = string.Empty;

    public async Task InitializeAsync()
    {
        _temp = Path.Combine(Path.GetTempPath(), "gravel-test-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);


        Engine = await GravelFactory.CreateFileSystemAsync(_temp);
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