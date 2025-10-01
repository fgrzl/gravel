using System.Diagnostics;
using FluentAssertions;
using Gravel.Internals;
using Xunit;

namespace Gravel.Engine;

// Ensure Timestamp is initialized using system time for deterministic tests
[Collection("SequenceTimestamp")]
public class SequenceTests
{
    static uint High(ulong seq) => (uint)(seq >> 32);
    static uint Low(ulong seq) => (uint)seq;

    [Fact]
    public void should_return_current_seconds_and_zero_counter_given_old_current_when_get_next()
    {
        // Arrange
        var nowMillis = Timestamp.GetTimestamp();
        var nowSec = (uint)(nowMillis / 1000);
        const ulong current = 0UL; // very old

        // Act
        var next = Sequence.GetNext(current);

        // Assert
        High(next).Should().Be(nowSec);
        Low(next).Should().Be(0);
        next.Should().BeGreaterThan(current);
    }

    [Fact]
    public void should_increment_low_counter_given_same_second_when_get_next()
    {
        // Arrange
        var nowMillis = Timestamp.GetTimestamp();
        var nowSec = (uint)(nowMillis / 1000);
        var initialCounter = 5u;
        var current = ((ulong)nowSec << 32) | initialCounter;

        // Act
        var next = Sequence.GetNext(current);

        // Assert
        High(next).Should().Be(nowSec);
        Low(next).Should().Be(initialCounter + 1);
        next.Should().BeGreaterThan(current);
    }

    [Fact]
    public void should_increment_counter_given_future_timestamp_when_get_next()
    {
        // Arrange: craft a current whose high seconds are in the future relative to Timestamp
        var nowMillis = Timestamp.GetTimestamp();
        var nowSec = (uint)(nowMillis / 1000);
        var futureSec = nowSec + 10;
        var counter = 123u;
        var current = ((ulong)futureSec << 32) | counter;

        // Act
        var next = Sequence.GetNext(current);

        // Assert: when system clock is behind current, Sequence increments low counter
        High(next).Should().Be(futureSec);
        Low(next).Should().Be(counter + 1);
        next.Should().BeGreaterThan(current);
    }

    [Fact]
    public void should_roll_to_next_second_given_counter_wrap_when_get_next()
    {
        // Arrange
        var nowMillis = Timestamp.GetTimestamp();
        var nowSec = (uint)(nowMillis / 1000);
        var current = ((ulong)nowSec << 32) | uint.MaxValue; // same second, counter at max

        // Act: call GetNext; it may block until the second advances (worst-case ~1s)
        var sw = Stopwatch.StartNew();
        var next = Sequence.GetNext(current);
        sw.Stop();

        // Assert
        High(next).Should().BeGreaterOrEqualTo(nowSec + 1);
        Low(next).Should().Be(0);
        next.Should().BeGreaterThan(current);
        sw.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(0);
    }
}