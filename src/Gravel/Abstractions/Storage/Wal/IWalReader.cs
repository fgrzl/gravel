namespace Gravel.Abstractions.Storage.Wal;

public interface IWalReader : IAsyncDisposable
{
    IAsyncEnumerable<WalRecord> ReplayAsync(CancellationToken ct = default);
}