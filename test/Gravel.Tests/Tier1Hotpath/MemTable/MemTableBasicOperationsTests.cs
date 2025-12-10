using System;
using System.Collections.Generic;
using System.Linq;
using Gravel.Abstractions;
using Gravel.Engine;
using Xunit;

namespace Gravel.Tests.Tier1Hotpath.MemTable;

/// <summary>
///     Tier 1: Hot path tests for MemTable in-memory operations.
///     Focus: O(log n) operations, sorting, tombstones.
///     Each test validates a single behavior with clear intent.
/// </summary>
public class MemTableBasicOperationsTests
{
    private Gravel.Engine.MemTable create_memtable() => new();

    // ============ Put Tests ============

    [Fact]
    public void put_should_store_key_value_pair()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();
        var value = "value"u8.ToArray();

        // Act
        memtable.Put(key, value, sequence: 1);

        // Assert
        var retrieved = memtable.Get(key);
        Assert.NotNull(retrieved);
        Assert.Equal(value, retrieved.Value.Value.ToArray());
    }

    [Fact]
    public void put_should_overwrite_existing_key()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();
        var value1 = "value1"u8.ToArray();
        var value2 = "value2"u8.ToArray();

        memtable.Put(key, value1, sequence: 1);

        // Act
        memtable.Put(key, value2, sequence: 2);

        // Assert
        var retrieved = memtable.Get(key);
        Assert.Equal(value2, retrieved.Value.Value.ToArray());
    }

    [Fact]
    public void put_should_update_sequence_on_overwrite()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();

        memtable.Put(key, "v1"u8.ToArray(), sequence: 10);

        // Act
        memtable.Put(key, "v2"u8.ToArray(), sequence: 20);

        // Assert
        var retrieved = memtable.Get(key);
        Assert.Equal(20UL, retrieved.Value.Sequence);
    }

    [Fact]
    public void put_should_track_approximate_size()
    {
        // Arrange
        var memtable = create_memtable();
        var initialSize = memtable.ApproximateSize;

        // Act
        memtable.Put("key"u8.ToArray(), "value"u8.ToArray(), sequence: 1);

        // Assert
        Assert.True(memtable.ApproximateSize > initialSize);
    }

    [Fact]
    public void put_should_handle_empty_key()
    {
        // Arrange
        var memtable = create_memtable();
        var key = Array.Empty<byte>();
        var value = "value"u8.ToArray();

        // Act
        memtable.Put(key, value, sequence: 1);

        // Assert
        var retrieved = memtable.Get(key);
        Assert.NotNull(retrieved);
        Assert.Equal(value, retrieved.Value.Value.ToArray());
    }

    [Fact]
    public void put_should_handle_empty_value()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();
        var value = Array.Empty<byte>();

        // Act
        memtable.Put(key, value, sequence: 1);

        // Assert
        var retrieved = memtable.Get(key);
        Assert.NotNull(retrieved);
        Assert.Empty(retrieved.Value.Value.ToArray());
    }

    // ============ Get Tests ============

    [Fact]
    public void get_should_return_stored_value()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();
        var value = "value"u8.ToArray();
        memtable.Put(key, value, sequence: 1);

        // Act
        var retrieved = memtable.Get(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(value, retrieved.Value.Value.ToArray());
    }

    [Fact]
    public void get_should_return_null_for_nonexistent_key()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        var retrieved = memtable.Get("nonexistent"u8.ToArray());

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void get_should_return_sequence_number()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();
        ulong sequence = 42;

        memtable.Put(key, "value"u8.ToArray(), sequence);

        // Act
        var retrieved = memtable.Get(key);

        // Assert
        Assert.Equal(sequence, retrieved.Value.Sequence);
    }

    // ============ Delete Tests ============

    [Fact]
    public void delete_should_store_tombstone()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();
        memtable.Put(key, "value"u8.ToArray(), sequence: 1);

        // Act
        memtable.Delete(key, sequence: 2);

        // Assert
        var retrieved = memtable.Get(key);
        Assert.NotNull(retrieved);
        Assert.Empty(retrieved.Value.Value.ToArray()); // Empty value = tombstone
    }

    [Fact]
    public void delete_should_update_sequence()
    {
        // Arrange
        var memtable = create_memtable();
        var key = "key"u8.ToArray();
        memtable.Put(key, "value"u8.ToArray(), sequence: 1);

        // Act
        memtable.Delete(key, sequence: 5);

        // Assert
        var retrieved = memtable.Get(key);
        Assert.Equal(5UL, retrieved.Value.Sequence);
    }

    [Fact]
    public void delete_should_handle_nonexistent_key()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        memtable.Delete("nonexistent"u8.ToArray(), sequence: 1);

        // Assert - should not throw
        var retrieved = memtable.Get("nonexistent"u8.ToArray());
        Assert.NotNull(retrieved); // Should have tombstone
    }

    // ============ DeleteRange Tests ============

    [Fact]
    public void delete_range_should_mark_boundaries()
    {
        // Arrange
        var memtable = create_memtable();
        var start = "key-03"u8.ToArray();
        var end = "key-07"u8.ToArray();

        // Act
        memtable.DeleteRange(start, end, sequence: 1);

        // Assert
        var startMarker = memtable.Get(start);
        var endMarker = memtable.Get(end);

        Assert.NotNull(startMarker);
        Assert.NotNull(endMarker);
        Assert.Empty(startMarker.Value.Value.ToArray()); // Tombstones
        Assert.Empty(endMarker.Value.Value.ToArray());
    }

    // ============ Count Tests ============

    [Fact]
    public void count_should_be_zero_when_empty()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        var count = memtable.Count;

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void count_should_increase_after_put()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        memtable.Put("key"u8.ToArray(), "value"u8.ToArray(), sequence: 1);

        // Assert
        Assert.Equal(1, memtable.Count);
    }

    [Fact]
    public void count_should_increase_for_each_unique_key()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        memtable.Put("k1"u8.ToArray(), "v1"u8.ToArray(), sequence: 1);
        memtable.Put("k2"u8.ToArray(), "v2"u8.ToArray(), sequence: 2);
        memtable.Put("k3"u8.ToArray(), "v3"u8.ToArray(), sequence: 3);

        // Assert
        Assert.Equal(3, memtable.Count);
    }

    [Fact]
    public void count_should_not_increase_on_overwrite()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("key"u8.ToArray(), "value1"u8.ToArray(), sequence: 1);

        // Act
        memtable.Put("key"u8.ToArray(), "value2"u8.ToArray(), sequence: 2);

        // Assert
        Assert.Equal(1, memtable.Count);
    }

    // ============ IsEmpty Tests ============

    [Fact]
    public void is_empty_should_be_true_initially()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        var isEmpty = memtable.IsEmpty;

        // Assert
        Assert.True(isEmpty);
    }

    [Fact]
    public void is_empty_should_be_false_after_put()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        memtable.Put("key"u8.ToArray(), "value"u8.ToArray(), sequence: 1);

        // Assert
        Assert.False(memtable.IsEmpty);
    }

    // ============ ShouldFlush Tests ============

    [Fact]
    public void should_flush_should_be_false_for_small_memtable()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("key"u8.ToArray(), "value"u8.ToArray(), sequence: 1);

        // Act
        var shouldFlush = memtable.ShouldFlush();

        // Assert
        Assert.False(shouldFlush);
    }

    // ============ GetEntries Tests ============

    [Fact]
    public void get_entries_should_return_all_entries()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("k1"u8.ToArray(), "v1"u8.ToArray(), sequence: 1);
        memtable.Put("k2"u8.ToArray(), "v2"u8.ToArray(), sequence: 2);
        memtable.Put("k3"u8.ToArray(), "v3"u8.ToArray(), sequence: 3);

        // Act
        var entries = memtable.GetEntries().ToList();

        // Assert
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void get_entries_should_include_puts()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("key"u8.ToArray(), "value"u8.ToArray(), sequence: 1);

        // Act
        var entries = memtable.GetEntries().ToList();

        // Assert
        Assert.Single(entries);
        Assert.Equal(DbEntryKind.Put, entries[0].Kind);
    }

    [Fact]
    public void get_entries_should_include_deletes_as_tombstones()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("key"u8.ToArray(), "value"u8.ToArray(), sequence: 1);
        memtable.Delete("key"u8.ToArray(), sequence: 2);

        // Act
        var entries = memtable.GetEntries().ToList();

        // Assert
        Assert.Single(entries);
        Assert.Equal(DbEntryKind.DeleteKey, entries[0].Kind);
    }

    [Fact]
    public void get_entries_should_be_empty_when_memtable_empty()
    {
        // Arrange
        var memtable = create_memtable();

        // Act
        var entries = memtable.GetEntries().ToList();

        // Assert
        Assert.Empty(entries);
    }

    // ============ Clear Tests ============

    [Fact]
    public void clear_should_reset_memtable()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("key"u8.ToArray(), "value"u8.ToArray(), sequence: 1);

        // Act
        memtable.Clear();

        // Assert
        Assert.Equal(0, memtable.Count);
        Assert.True(memtable.IsEmpty);
        Assert.Equal(0, memtable.ApproximateSize);
    }

    [Fact]
    public void clear_should_allow_reuse()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("k1"u8.ToArray(), "v1"u8.ToArray(), sequence: 1);
        memtable.Clear();

        // Act
        memtable.Put("k2"u8.ToArray(), "v2"u8.ToArray(), sequence: 2);

        // Assert
        var retrieved = memtable.Get("k2"u8.ToArray());
        Assert.NotNull(retrieved);
        Assert.Equal("v2"u8.ToArray(), retrieved.Value.Value.ToArray());
    }

    // ============ Lexicographic Ordering Tests ============

    [Fact]
    public void entries_should_be_returned_in_sorted_order()
    {
        // Arrange
        var memtable = create_memtable();
        memtable.Put("c"u8.ToArray(), "vc"u8.ToArray(), sequence: 3);
        memtable.Put("a"u8.ToArray(), "va"u8.ToArray(), sequence: 1);
        memtable.Put("b"u8.ToArray(), "vb"u8.ToArray(), sequence: 2);

        // Act
        var entries = memtable.GetEntries().ToList();

        // Assert
        Assert.Equal("a"u8.ToArray(), entries[0].Key.ToArray());
        Assert.Equal("b"u8.ToArray(), entries[1].Key.ToArray());
        Assert.Equal("c"u8.ToArray(), entries[2].Key.ToArray());
    }
}
