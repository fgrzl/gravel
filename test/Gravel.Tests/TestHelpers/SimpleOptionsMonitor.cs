using System;
using Microsoft.Extensions.Options;

namespace Gravel.TestHelpers;

sealed class SimpleOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public SimpleOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable OnChange(Action<T, string> listener)
    {
        return new DummyDisposable();
    }

    sealed class DummyDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}