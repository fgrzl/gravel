namespace Gravel.Abstractions.Storage.Wal;

/// <summary>
///     Factory interface for creating Write-Ahead Log (WAL) readers and writers.
/// </summary>
public interface IWalFactory
{
    /// <summary>
    ///     Creates a WAL reader for the specified directory.
    /// </summary>
    /// <param name="directory">The directory containing WAL segments.</param>
    /// <returns>An <see cref="IWalReader" /> instance for replaying WAL records.</returns>
    IWalReader CreateReader(string directory);

    /// <summary>
    ///     Creates a WAL writer for the specified directory.
    /// </summary>
    /// <param name="directory">The directory to write WAL segments to.</param>
    /// <returns>An <see cref="IWalWriter" /> instance for appending WAL records.</returns>
    IWalWriter CreateWriter(string directory);
}
