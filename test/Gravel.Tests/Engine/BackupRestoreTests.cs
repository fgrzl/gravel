using System.IO;
using System.Text;
using System.Threading.Tasks;
using Gravel.TestHelpers;
using Xunit;

namespace Gravel.Engine;

public class BackupRestoreTests
{
    [Fact]
    public async Task should_backup_and_restore_file_system_database()
    {
        // Arrange: create temp directories for original DB and restore target, plus archive path
        using var src = new TempDirectory("gravel-src-");
        using var dst = new TempDirectory("gravel-dst-");
        var archive = Path.Combine(Path.GetTempPath(), "gravel_backup_" + Path.GetRandomFileName() + ".zip");

        // Create an engine backed by the filesystem and put some data
        var eng = await GravelFactory.CreateFileSystemAsync(src.Path);
        await eng.PutAsync("k1"u8.ToArray(), "v1"u8.ToArray());
        await eng.PutAsync("k2"u8.ToArray(), "v2"u8.ToArray());

        // Act: create backup archive and dispose engine
        await eng.BackupAsync(archive, new BackupOptions { IncludeWalSegments = false });
        await eng.DisposeAsync();

        // Use a separate engine instance for the restore target; dispose it so RestoreAsync is allowed
        var restoreEngineHolder = await GravelFactory.CreateFileSystemAsync(dst.Path);
        await restoreEngineHolder.DisposeAsync();

        // Perform restore into the restore directory
        await restoreEngineHolder.RestoreAsync(archive);

        // Open a new engine against the restored directory and verify data
        var restored = await GravelFactory.CreateFileSystemAsync(dst.Path);
        var g1 = await restored.GetAsync("k1"u8.ToArray());
        var g2 = await restored.GetAsync("k2"u8.ToArray());

        // Assert
        Assert.True(g1.HasValue);
        Assert.Equal("v1", Encoding.UTF8.GetString(g1!.Value.Span));
        Assert.True(g2.HasValue);
        Assert.Equal("v2", Encoding.UTF8.GetString(g2!.Value.Span));

        // Cleanup
        await restored.DisposeAsync();

        try
        {
            File.Delete(archive);
        }
        catch
        {
        }
    }
}
