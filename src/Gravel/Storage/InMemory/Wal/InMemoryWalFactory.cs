using Gravel.Abstractions.Storage.Wal;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.InMemory.Wal;

/// <summary>
///     Factory for creating in-memory WAL readers and writers.
///     Supports shared or per-directory writer instances based on options.
/// </summary>
public sealed class InMemoryWalFactory : IWalFactory
{
    readonly InMemoryWalOptions _options;
    readonly InMemoryWalWriter? _shared;

    /// <summary>
    ///     Initializes a new instance of <see cref="InMemoryWalFactory" />.
    /// </summary>
    /// <param name="options">The in-memory WAL options.</param>
    public InMemoryWalFactory(IOptions<InMemoryWalOptions> options)
    {
        _options = options.Value;
        if (_options.SharedWriter)
            _shared = new InMemoryWalWriter(_options.MaxBufferedRecords);
    }

    /// <summary>
    ///     Creates a WAL reader for the specified directory.
    ///     Uses a shared or new writer instance based on options.
    /// </summary>
    /// <param name="directory">The directory (ignored for in-memory).</param>
    /// <returns>An <see cref="IWalReader" /> instance.</returns>
    public IWalReader CreateReader(string directory)
    {
        var writer = _options.SharedWriter ? _shared! : new InMemoryWalWriter(_options.MaxBufferedRecords);
        return new InMemoryWalReader(writer);
    }

    /// <summary>
    ///     Creates a WAL writer for the specified directory.
    ///     Uses a shared or new writer instance based on options.
    /// </summary>
    /// <param name="directory">The directory (ignored for in-memory).</param>
    /// <returns>An <see cref="IWalWriter" /> instance.</returns>
    public IWalWriter CreateWriter(string directory)
    {
        return _options.SharedWriter ? _shared! : new InMemoryWalWriter(_options.MaxBufferedRecords);
    }
}
