using Gravel.Abstractions.Storage.Wal;
using Microsoft.Extensions.Options;

namespace Gravel.Storage.InMemory.Wal;

public sealed class InMemoryWalFactory : IWalFactory
{
    readonly InMemoryWalOptions _options;
    readonly InMemoryWalWriter? _shared;

    public InMemoryWalFactory(IOptions<InMemoryWalOptions> options)
    {
        _options = options.Value;
        if (_options.SharedWriter)
            _shared = new InMemoryWalWriter(_options.MaxBufferedRecords);
    }

    public IWalReader CreateReader(string directory)
    {
        var writer = _options.SharedWriter ? _shared! : new InMemoryWalWriter(_options.MaxBufferedRecords);
        return new InMemoryWalReader(writer);
    }

    public IWalWriter CreateWriter(string directory)
    {
        return _options.SharedWriter ? _shared! : new InMemoryWalWriter(_options.MaxBufferedRecords);
    }
}
