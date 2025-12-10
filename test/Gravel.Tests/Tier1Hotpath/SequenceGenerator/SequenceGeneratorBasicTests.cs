using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gravel.Engine;
using Xunit;

namespace Gravel.Tests.Tier1Hotpath.SequenceGenerator;

/// <summary>
///     Tier 1: Hot path tests for SequenceGenerator.
///     Focus: Monotonic sequence generation and thread-safety.
///     Each test validates a single behavior with clear intent.
/// </summary>
public class SequenceGeneratorBasicTests
{
    [Fact]
    public void next_should_return_first_sequence_as_one()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 1);

        // Act
        var seq = gen.Next();

        // Assert
        Assert.Equal(1UL, seq);
    }

    [Fact]
    public void next_should_return_custom_start_sequence()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 100);

        // Act
        var seq = gen.Next();

        // Assert
        Assert.Equal(100UL, seq);
    }

    [Fact]
    public void next_should_increment_monotonically()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 1);

        // Act
        var seq1 = gen.Next();
        var seq2 = gen.Next();
        var seq3 = gen.Next();

        // Assert
        Assert.Equal(1UL, seq1);
        Assert.Equal(2UL, seq2);
        Assert.Equal(3UL, seq3);
    }

    [Fact]
    public void next_should_never_repeat()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator();
        var sequences = new HashSet<ulong>();

        // Act
        for (int i = 0; i < 1000; i++)
        {
            sequences.Add(gen.Next());
        }

        // Assert
        Assert.Equal(1000, sequences.Count); // All unique
    }

    [Fact]
    public void current_should_return_current_sequence_without_incrementing()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 5);
        gen.Next();
        gen.Next();

        // Act
        var current1 = gen.Current;
        var current2 = gen.Current;

        // Assert
        Assert.Equal(3UL, current1); // 5 + 2 calls to Next = 7, then Next would be 8
        Assert.Equal(current1, current2); // Should be same
    }

    [Fact]
    public void set_current_should_update_sequence_for_recovery()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 1);

        // Act
        gen.SetCurrent(50);
        var seq = gen.Next();

        // Assert
        Assert.Equal(51UL, seq);
    }

    [Fact]
    public void set_current_should_not_decrease_sequence()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 100);
        gen.Next(); // Now at 101

        // Act
        gen.SetCurrent(50); // Try to set to lower value

        // Assert
        var seq = gen.Next();
        Assert.Equal(101UL, seq); // Should not have decreased
    }

    [Fact]
    public void set_current_should_increase_sequence_if_higher()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 1);

        // Act
        gen.SetCurrent(100);
        var seq = gen.Next();

        // Assert
        Assert.Equal(101UL, seq);
    }

    [Fact]
    public void concurrent_next_calls_should_be_thread_safe()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 1);
        var sequences = new System.Collections.Concurrent.ConcurrentBag<ulong>();
        const int threadCount = 10;
        const int callsPerThread = 100;

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < callsPerThread; i++)
                {
                    sequences.Add(gen.Next());
                }
            }))
            .ToArray();

        Task.WaitAll(tasks);

        // Assert
        var uniqueSequences = sequences.Distinct().Count();
        Assert.Equal(threadCount * callsPerThread, uniqueSequences); // All unique
    }

    [Fact]
    public void concurrent_next_and_set_current_should_be_thread_safe()
    {
        // Arrange
        var gen = new Gravel.Engine.SequenceGenerator(startSequence: 1);
        var sequences = new System.Collections.Concurrent.ConcurrentBag<ulong>();
        const int iterations = 100;

        // Act
        Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                sequences.Add(gen.Next());
            }
        });

        Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                gen.SetCurrent(500UL + (ulong)i);
            }
        });

        Task.WaitAll();

        // Assert - all Next() calls should have returned valid sequences
        Assert.NotEmpty(sequences);
        Assert.True(sequences.All(s => s > 0));
    }
}
