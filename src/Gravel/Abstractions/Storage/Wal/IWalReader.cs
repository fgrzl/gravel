namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Interface for reading Write-Ahead Log (WAL) records asynchronously.
/// </summary>
public interface IWalReader : IAsyncDisposable
{
    /// <summary>
    ///     Replays WAL records asynchronously, yielding each <see cref="WalRecord" /> in order.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of <see cref="WalRecord" />.</returns>
    IAsyncEnumerable<WalRecord> ReplayAsync(CancellationToken ct = default);
}
