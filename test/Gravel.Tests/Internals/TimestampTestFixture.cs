using System;

namespace Gravel.Internals;

public sealed class TimestampTestFixture : IDisposable
{
    const string Env = "GRAVEL_TIME_SERVER";
    readonly string? _prev;

    public TimestampTestFixture()
    {
        // Ensure the Timestamp static ctor initializes using system time (no network).
        _prev = Environment.GetEnvironmentVariable(Env);
        Environment.SetEnvironmentVariable(Env, "system");

        // Force type initialization now under controlled env
        _ = Timestamp.GetInitializationError();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Env, _prev);
    }
}
