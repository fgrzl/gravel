using System;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Gravel.Internals;

// Disable parallelization for this collection to avoid race conditions
// around environment variables and static initialization.

[Collection("TimestampTests")]
public class TimestampTests
{
    [Fact]
    public void should_not_error_on_initialization_when_env_is_system()
    {
        // Arrange: fixture has set env to 'system' and forced initialization

        // Act
        var ex = Timestamp.GetInitializationError();

        // Assert
        Assert.Null(ex);
    }

    [Fact]
    public void should_return_time_server_from_env()
    {
        // Arrange
        const string envVar = "GRAVEL_TIME_SERVER";
        var prev = Environment.GetEnvironmentVariable(envVar);
        try
        {
            Environment.SetEnvironmentVariable(envVar, "custom.server");

            // Act
            var server = Timestamp.GetTimeServer();

            // Assert
            Assert.Equal("custom.server", server);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, prev);
        }
    }

    [Fact]
    public void should_return_non_null_time_server_from_fixture()
    {
        // Arrange: fixture set env to 'system'

        // Act
        var server = Timestamp.GetTimeServer();

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public void should_provide_monotonic_timestamp()
    {
        // Arrange

        // Act
        var t1 = Timestamp.GetTimestamp();
        Thread.Sleep(5);
        var t2 = Timestamp.GetTimestamp();

        // Assert
        Assert.True(t2 >= t1);
    }

    [Fact]
    public void should_track_wall_clock_within_tolerance()
    {
        // Arrange

        // Act
        var t = Timestamp.GetTimestamp();

        // Assert: sanity vs current UTC time (allow generous skew to avoid flakiness)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(Math.Abs(t - now) < 10_000); // within 10s
    }

    [Fact]
    public void should_use_system_time_for_getcurrenttime_when_env_is_system()
    {
        // Arrange
        const string envVar = "GRAVEL_TIME_SERVER";
        var prev = Environment.GetEnvironmentVariable(envVar);
        try
        {
            Environment.SetEnvironmentVariable(envVar, "system");
            var mi = typeof(Timestamp).GetMethod("GetCurrentTime", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(mi);

            // Act
            var result = (DateTime)mi!.Invoke(null, [])!;

            // Assert
            var delta = Math.Abs((result - DateTime.UtcNow).TotalSeconds);
            Assert.True(delta < 2); // within 2 seconds
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, prev);
        }
    }

    [Fact]
    public void should_return_false_for_invalid_ntp_host()
    {
        // Arrange
        var mi = typeof(Timestamp).GetMethod("TryGetNtpTime", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mi);

        var args = new object?[] { "invalid.invalid", default(DateTime) };

        // Act
        var ok = (bool)mi!.Invoke(null, args)!;

        // Assert
        Assert.False(ok);
        Assert.Equal(default, (DateTime)args[1]!);
    }

    [Fact]
    public void should_fallback_to_system_when_custom_server_fails()
    {
        // Arrange
        var envVar = "GRAVEL_TIME_SERVER";
        var prev = Environment.GetEnvironmentVariable(envVar);
        try
        {
            // Set to a hostname that should not resolve
            Environment.SetEnvironmentVariable(envVar, "invalid.invalid");
            var mi = typeof(Timestamp).GetMethod("GetCurrentTime", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(mi);

            // Act
            var result = (DateTime)mi!.Invoke(null, [])!;

            // Assert
            var delta = Math.Abs((result - DateTime.UtcNow).TotalSeconds);
            Assert.True(delta < 2); // fell back to system clock
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, prev);
        }
    }
}
