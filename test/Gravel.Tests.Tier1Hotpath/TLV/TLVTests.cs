using Xunit;
using Gravel.Storage.TLV;
using System.Text;

namespace Gravel.Tests.Tier1Hotpath;

/// <summary>
///     Tier 1: TLV hot path tests - validates critical read/write operations
/// </summary>
public class TLVReaderTests
{
    [Fact]
    public void TryReadEntry_WithValidPutEntry_ReturnsTrue()
    {
        // Arrange
        var buffer = new byte[100];
        var key = Encoding.UTF8.GetBytes("test-key");
        var value = Encoding.UTF8.GetBytes("test-value");
        
        var written = TLVFormat.WriteEntry(buffer, 0, TLVFormat.TypePut, key, value);
        Assert.True(written > 0);

        // Act
        var result = TLVFormat.TryReadEntry(buffer, 0, out var type, out var readKey, out var readValue, out var bytesRead);

        // Assert
        Assert.True(result);
        Assert.Equal(TLVFormat.TypePut, type);
        Assert.Equal(key, readKey.ToArray());
        Assert.Equal(value, readValue.ToArray());
        Assert.Equal(written, bytesRead);
    }

    [Fact]
    public void TLVReader_EnumerateAll_ReadsAllEntries()
    {
        // Arrange
        var ms = new MemoryStream();
        using (var writer = new TLVWriter(ms, leaveOpen: true))
        {
            for (int i = 0; i < 10; i++)
            {
                var key = Encoding.UTF8.GetBytes($"key-{i}");
                var value = Encoding.UTF8.GetBytes($"value-{i}");
                writer.WriteEntryAsync(TLVFormat.TypePut, key, value).GetAwaiter().GetResult();
            }
            writer.WriteEndMarkerAsync().GetAwaiter().GetResult();
            writer.FlushAsync().GetAwaiter().GetResult();
        }

        var buffer = ms.ToArray();
        var reader = new TLVReader(buffer);

        // Act
        var entries = reader.EnumerateAll().ToList();

        // Assert
        Assert.Equal(10, entries.Count);
    }
}

public class TLVWriterTests
{
    [Fact]
    public void WriteEntry_WithPutType_WritesCorrectFormat()
    {
        // Arrange
        var buffer = new byte[100];
        var key = Encoding.UTF8.GetBytes("key");
        var value = Encoding.UTF8.GetBytes("value");

        // Act
        var written = TLVFormat.WriteEntry(buffer, 0, TLVFormat.TypePut, key, value);

        // Assert
        Assert.True(written > 0);
        Assert.Equal(TLVFormat.TypePut, buffer[0]);
    }
}
