using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Gravel.Abstractions;
using Gravel.Engine;
using Gravel.Storage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gravel.Tests.Tier1Hotpath.Engine;

/// <summary>
///     Tier 1: Hot path tests for DbEngine CRUD operations.
///     Focus: Core read/write latency and correctness.
///     Each test validates a single behavior with clear intent.
/// </summary>
public class DbEngineBasicCrudTests : IAsyncLifetime
{
    private StorageInstance _storage = null!;
    private DbEngine _engine = null!;
    private ILogger<DbEngineBasicCrudTests> _logger = null!;

    public async Task InitializeAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gravel-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var config = new StorageConfig
        {
            Mode = StorageMode.LocalOnly,
            LocalPath = tempDir,
            WalSegmentSizeBytes = 10 * 1024 * 1024
        };

        _storage = StorageFactory.Create(config, null, null);
        _engine = new DbEngine(_storage, null);
        
        _logger = null!; // For reference in tests if needed
    }

    public async Task DisposeAsync()
    {
        if (_engine != null)
            await _engine.DisposeAsync();

        if (_storage != null)
            await _storage.DisposeAsync();
    }

    // ============ Put Tests ============

    [Fact]
    public async Task should_store_key_value_pair_on_put()
    {
        // Arrange
        var key = "test-key"u8.ToArray();
        var value = "test-value"u8.ToArray();

        // Act
        await _engine.PutAsync(key, value);

        // Assert
        var retrieved = await _engine.GetAsync(key);
        Assert.True(retrieved.HasValue);
        Assert.Equal(value, retrieved.Value.ToArray());
    }

    [Fact]
    public async Task should_overwrite_existing_value_on_put()
    {
        // Arrange
        var key = "key"u8.ToArray();
        var value1 = "value1"u8.ToArray();
        var value2 = "value2"u8.ToArray();

        await _engine.PutAsync(key, value1);

        // Act
        await _engine.PutAsync(key, value2);

        // Assert
        var retrieved = await _engine.GetAsync(key);
        Assert.Equal(value2, retrieved.Value.ToArray());
    }

    [Fact]
    public async Task should_handle_empty_key_on_put()
    {
        // Arrange
        var key = Array.Empty<byte>();
        var value = "value"u8.ToArray();

        // Act
        await _engine.PutAsync(key, value);

        // Assert
        var retrieved = await _engine.GetAsync(key);
        Assert.True(retrieved.HasValue);
        Assert.Equal(value, retrieved.Value.ToArray());
    }

    [Fact]
    public async Task should_handle_empty_value_on_put()
    {
        // Arrange
        var key = "key"u8.ToArray();
        var value = Array.Empty<byte>();

        // Act
        await _engine.PutAsync(key, value);

        // Assert
        var retrieved = await _engine.GetAsync(key);
        Assert.True(retrieved.HasValue);
        Assert.Empty(retrieved.Value.ToArray());
    }

    [Fact]
    public async Task should_handle_large_values_on_put()
    {
        // Arrange
        var key = "large"u8.ToArray();
        var largeValue = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(largeValue);

        // Act
        await _engine.PutAsync(key, largeValue);

        // Assert
        var retrieved = await _engine.GetAsync(key);
        Assert.Equal(largeValue, retrieved.Value.ToArray());
    }

    // ============ Get Tests ============

    [Fact]
    public async Task should_return_stored_value_on_get()
    {
        // Arrange
        var key = "mykey"u8.ToArray();
        var value = "myvalue"u8.ToArray();
        await _engine.PutAsync(key, value);

        // Act
        var retrieved = await _engine.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(value, retrieved.Value.ToArray());
    }

    [Fact]
    public async Task should_return_null_for_nonexistent_key_on_get()
    {
        // Arrange
        var key = "nonexistent"u8.ToArray();

        // Act
        var result = await _engine.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task should_return_null_for_deleted_key_on_get()
    {
        // Arrange
        var key = "key"u8.ToArray();
        var value = "value"u8.ToArray();
        await _engine.PutAsync(key, value);
        await _engine.DeleteAsync(key);

        // Act
        var result = await _engine.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    // ============ Delete Tests ============

    [Fact]
    public async Task should_remove_existing_key_on_delete()
    {
        // Arrange
        var key = "key-to-delete"u8.ToArray();
        var value = "value"u8.ToArray();
        await _engine.PutAsync(key, value);

        // Act
        var deleted = await _engine.DeleteAsync(key);

        // Assert
        Assert.True(deleted);
        var retrieved = await _engine.GetAsync(key);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task should_return_false_for_nonexistent_key_on_delete()
    {
        // Arrange
        var key = "nonexistent"u8.ToArray();

        // Act
        var deleted = await _engine.DeleteAsync(key);

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public async Task should_return_false_when_key_already_deleted_on_delete()
    {
        // Arrange
        var key = "key"u8.ToArray();
        await _engine.PutAsync(key, "value"u8.ToArray());
        await _engine.DeleteAsync(key);

        // Act
        var deleted = await _engine.DeleteAsync(key);

        // Assert
        Assert.False(deleted);
    }

    // ============ Exists Tests ============

    [Fact]
    public async Task should_return_true_for_existing_key_on_exists()
    {
        // Arrange
        var key = "existing"u8.ToArray();
        var value = "value"u8.ToArray();
        await _engine.PutAsync(key, value);

        // Act
        var exists = await _engine.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task should_return_false_for_nonexistent_key_on_exists()
    {
        // Arrange
        var key = "nonexistent"u8.ToArray();

        // Act
        var exists = await _engine.ExistsAsync(key);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task should_return_false_for_deleted_key_on_exists()
    {
        // Arrange
        var key = "key"u8.ToArray();
        await _engine.PutAsync(key, "value"u8.ToArray());
        await _engine.DeleteAsync(key);

        // Act
        var exists = await _engine.ExistsAsync(key);

        // Assert
        Assert.False(exists);
    }

    // ============ Insert Tests ============

    [Fact]
    public async Task should_store_new_key_on_insert()
    {
        // Arrange
        var key = "newkey"u8.ToArray();
        var value = "value"u8.ToArray();

        // Act
        await _engine.InsertAsync(key, value);

        // Assert
        var retrieved = await _engine.GetAsync(key);
        Assert.Equal(value, retrieved.Value.ToArray());
    }

    [Fact]
    public async Task should_throw_when_key_exists_on_insert()
    {
        // Arrange
        var key = "existing"u8.ToArray();
        var value1 = "value1"u8.ToArray();
        var value2 = "value2"u8.ToArray();
        await _engine.PutAsync(key, value1);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _engine.InsertAsync(key, value2)
        );
    }

    // ============ Batch Tests ============

    [Fact]
    public async Task should_apply_multiple_puts_on_batch()
    {
        // Arrange
        var mutations = new List<Mutation>
        {
            Mutation.Put("k1"u8.ToArray(), "v1"u8.ToArray()),
            Mutation.Put("k2"u8.ToArray(), "v2"u8.ToArray()),
            Mutation.Put("k3"u8.ToArray(), "v3"u8.ToArray())
        };

        // Act
        await _engine.BatchAsync(mutations);

        // Assert
        var v1 = await _engine.GetAsync("k1"u8.ToArray());
        var v2 = await _engine.GetAsync("k2"u8.ToArray());
        var v3 = await _engine.GetAsync("k3"u8.ToArray());

        Assert.Equal("v1"u8.ToArray(), v1.Value.ToArray());
        Assert.Equal("v2"u8.ToArray(), v2.Value.ToArray());
        Assert.Equal("v3"u8.ToArray(), v3.Value.ToArray());
    }

    [Fact]
    public async Task should_apply_mixed_put_and_delete_on_batch()
    {
        // Arrange
        await _engine.PutAsync("initial"u8.ToArray(), "value"u8.ToArray());

        var mutations = new List<Mutation>
        {
            Mutation.Put("new"u8.ToArray(), "val"u8.ToArray()),
            Mutation.Delete("initial"u8.ToArray())
        };

        // Act
        await _engine.BatchAsync(mutations);

        // Assert
        var newVal = await _engine.GetAsync("new"u8.ToArray());
        var deleted = await _engine.GetAsync("initial"u8.ToArray());

        Assert.NotNull(newVal);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task should_apply_operations_in_order_on_batch()
    {
        // Arrange
        var mutations = new List<Mutation>
        {
            Mutation.Put("key"u8.ToArray(), "first"u8.ToArray()),
            Mutation.Put("key"u8.ToArray(), "second"u8.ToArray())
        };

        // Act
        await _engine.BatchAsync(mutations);

        // Assert
        var result = await _engine.GetAsync("key"u8.ToArray());
        Assert.Equal("second"u8.ToArray(), result.Value.ToArray());
    }

    // ============ DeleteRange Tests ============

    [Fact]
    public async Task should_remove_keys_in_range_on_delete_range()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var key = Encoding.UTF8.GetBytes($"key-{i:D2}");
            var value = Encoding.UTF8.GetBytes($"val-{i}");
            await _engine.PutAsync(key, value);
        }

        // Act
        await _engine.DeleteRangeAsync("key-03"u8.ToArray(), "key-07"u8.ToArray());

        // Assert
        var before = await _engine.GetAsync("key-02"u8.ToArray());
        var inRange1 = await _engine.GetAsync("key-03"u8.ToArray());
        var inRange2 = await _engine.GetAsync("key-05"u8.ToArray());
        var after = await _engine.GetAsync("key-07"u8.ToArray());

        Assert.NotNull(before);
        Assert.Null(inRange1);
        Assert.Null(inRange2);
        Assert.NotNull(after);
    }

    // ============ Stress Tests ============

    [Fact]
    public async Task should_maintain_correctness_on_sequential_puts()
    {
        // Arrange
        const int count = 100;

        // Act
        for (int i = 0; i < count; i++)
        {
            var key = Encoding.UTF8.GetBytes($"key-{i}");
            var value = Encoding.UTF8.GetBytes($"value-{i}");
            await _engine.PutAsync(key, value);
        }

        // Assert
        for (int i = 0; i < count; i++)
        {
            var key = Encoding.UTF8.GetBytes($"key-{i}");
            var expected = Encoding.UTF8.GetBytes($"value-{i}");
            var retrieved = await _engine.GetAsync(key);

            Assert.True(retrieved.HasValue, $"Key {i} not found");
            Assert.Equal(expected, retrieved.Value.ToArray());
        }
    }
}
