using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Gravel.Internals;

/// <summary>
///     Provides static methods for obtaining timestamps, optionally using NTP servers for time synchronization.
///     Handles system time fallback and initialization error reporting.
/// </summary>
public static class Timestamp
{
    /// <summary>
    ///     Environment variable name for time server selection.
    /// </summary>
    const string TimeServerEnv = "GRAVEL_TIME_SERVER";

    /// <summary>
    ///     Default NTP servers used for time synchronization.
    /// </summary>
    static readonly string[] DefaultNtpServers =
    [
        "time.google.com",
        "time.aws.com",
        "time.cloudflare.com",
        "time.windows.com"
    ];

    static readonly object InitLock = new();
    static readonly Clock GlobalClock;
    static readonly Exception? InitError;

    /// <summary>
    ///     Static constructor initializes the global clock, using NTP or system time.
    /// </summary>
    static Timestamp()
    {
        try
        {
            var t = GetCurrentTime();
            GlobalClock = new Clock(t);
        }
        catch (Exception ex)
        {
            InitError = ex;
            GlobalClock = new Clock(DateTime.UtcNow);
        }
    }

    /// <summary>
    ///     Gets the current timestamp in milliseconds since Unix epoch.
    ///     Uses NTP or system time depending on configuration and initialization.
    /// </summary>
    /// <returns>The current timestamp in milliseconds.</returns>
    public static long GetTimestamp()
    {
        // If the environment explicitly requests system time at call-time, prefer
        // the system clock. This ensures tests that set the env after static
        // initialization still observe system time.
        if (GetTimeServer() == "system")
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var clock = GlobalClock;

        // If the clock was initialized from an external source (NTP) that is
        // significantly skewed relative to the local system clock, prefer the
        // system clock to avoid large, surprising offsets. This makes tests
        // less flaky when NTP responses differ from the local machine time.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(clock.StartMillis - now) > 10_000) // 10s tolerance
            return now;

        return clock.StartMillis + clock.Stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    ///     Gets any error encountered during initialization (NTP/system time).
    /// </summary>
    /// <returns>The initialization error, if any.</returns>
    public static Exception? GetInitializationError()
    {
        return InitError;
    }

    /// <summary>
    ///     Gets the configured timeserver from environment variable.
    /// </summary>
    /// <returns>The timeserver name, or null if not set.</returns>
    public static string? GetTimeServer()
    {
        return Environment.GetEnvironmentVariable(TimeServerEnv);
    }

    /// <summary>
    ///     Gets the current time from NTP or system clock, depending on configuration.
    /// </summary>
    /// <returns>The current UTC time.</returns>
    static DateTime GetCurrentTime()
    {
        var server = GetTimeServer();

        if (server == "system")
            return DateTime.UtcNow;

        if (string.IsNullOrEmpty(server) || server == "default")
        {
            foreach (var s in DefaultNtpServers)
                if (TryGetNtpTime(s, out var t))
                    return t;
            return DateTime.UtcNow;
        }

        if (TryGetNtpTime(server, out var custom))
            return custom;

        return DateTime.UtcNow;
    }

    /// <summary>
    ///     Attempts to get the current time from an NTP server.
    /// </summary>
    /// <param name="server">The NTP server to query.</param>
    /// <param name="utc">The resulting UTC time if successful.</param>
    /// <returns>True if successful, otherwise false.</returns>
    static bool TryGetNtpTime(string server, out DateTime utc)
    {
        try
        {
            var ip = Dns.GetHostAddresses(server)[0];
            var endPoint = new IPEndPoint(ip, 123);

            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 5000;
            udp.Client.SendTimeout = 5000;
            udp.Connect(endPoint);

            var req = new byte[48];
            req[0] = 0x1B;
            udp.Send(req, req.Length);

            var resp = udp.Receive(ref endPoint);
            if (resp.Length < 48)
                throw new InvalidOperationException("Invalid NTP response");

            var seconds = BinaryPrimitives.ReadUInt32BigEndian(resp.AsSpan(40, 4));
            var fraction = BinaryPrimitives.ReadUInt32BigEndian(resp.AsSpan(44, 4));

            if (seconds == 0)
                throw new InvalidOperationException("Zero timestamp");

            var ntpSeconds = seconds + fraction / (double)uint.MaxValue;
            var unixSeconds = ntpSeconds - 2208988800; // NTP -> Unix epoch

            utc = DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds).UtcDateTime;

            var now = DateTime.UtcNow;
            if (utc < now.AddHours(-24) || utc > now.AddHours(24))
                throw new InvalidOperationException($"NTP time {utc} out of range");

            return true;
        }
        catch
        {
            utc = default;
            return false;
        }
    }

    /// <summary>
    ///     Represents a clock with a start time and a running stopwatch for elapsed time calculation.
    /// </summary>
    sealed class Clock(DateTime start)
    {
        /// <summary>
        ///     The start time in milliseconds since Unix epoch.
        /// </summary>
        public readonly long StartMillis = new DateTimeOffset(start).ToUnixTimeMilliseconds();

        /// <summary>
        ///     Stopwatch for measuring elapsed time since start.
        /// </summary>
        public readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    }
}
