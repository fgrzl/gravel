using System;
using Microsoft.Extensions.Options;

namespace Gravel.Tests.TestHelpers;

sealed class SimpleOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    readonly T _value;
    public SimpleOptionsMonitor(T value) => _value = value;
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string> listener) => new DummyDisposable();
    sealed class DummyDisposable : IDisposable { public void Dispose() { } }
}