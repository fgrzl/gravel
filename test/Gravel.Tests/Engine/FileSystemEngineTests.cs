using System.IO;
using System.Threading.Tasks;
using Gravel.TestHelpers;
using Xunit;

namespace Gravel.Engine;

public sealed class FileSystemEngineTests : EngineTestsBase, IAsyncLifetime
{
    string _temp = string.Empty;

    public async Task InitializeAsync()
    {
        var td = new TempDirectory("gravel-test-db-");
        _temp = td.Path;

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
