namespace Gravel.Abstractions.Storage.Wal;

public interface IWalFactory
{
    IWalReader CreateReader(string directory);
    IWalWriter CreateWriter(string directory);
}