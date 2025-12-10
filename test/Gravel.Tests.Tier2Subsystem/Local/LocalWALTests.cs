using Xunit;
using Gravel.Storage.Local;
using Gravel.Abstractions;
using System.Text;

namespace Gravel.Tests.Tier2Subsystem;

/// <summary>
///     Tier 2: LocalWAL subsystem tests
/// </summary>
public class LocalWALTests
{
    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gravel-wal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task AppendAsync_WithSingleEntry_StoresEntry()
    {
        // Arrange
        var dir = CreateTempDir();
        try
        {
            using (var wal = new LocalWAL(dir))
            {
                var entry = DbEntry.Put(
                    Encoding.UTF8.GetBytes("test-key"),
                    Encoding.UTF8.GetBytes("test-value"),
                    1UL
                );

                // Act
                await wal.AppendAsync(entry);

                // Assert
                Assert.Equal(1UL, wal.LastDurableSequence);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RecoverAsync_WithStoredEntries_ReturnsAllEntries()
    {
        // Arrange
        var dir = CreateTempDir();
        try
        {
            List<DbEntry> stored = new();
            using (var wal = new LocalWAL(dir))
            {
                for (int i = 0; i < 5; i++)
                {
                    var entry = DbEntry.Put(
                        Encoding.UTF8.GetBytes($"key-{i}"),
                        Encoding.UTF8.GetBytes($"value-{i}"),
                        (ulong)(i + 1)
                    );
                    await wal.AppendAsync(entry);
                    stored.Add(entry);
                }
                await wal.FlushAsync();
            }

            // Act
            using (var wal = new LocalWAL(dir))
            {
                var recovered = new List<DbEntry>();
                await foreach (var entry in wal.RecoverAsync())
                {
                    recovered.Add(entry);
                }

                // Assert
                Assert.Equal(stored.Count, recovered.Count);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
