using System;

namespace Gravel.Internals;

public sealed class SequenceTimestampFixture : IDisposable
{
    const string Env = "GRAVEL_TIME_SERVER";
    readonly string? _prev;

    public SequenceTimestampFixture()
    {
        _prev = Environment.GetEnvironmentVariable(Env);
        Environment.SetEnvironmentVariable(Env, "system");
        // force Timestamp type initializer
        _ = Timestamp.GetInitializationError();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Env, _prev);
    }
}