using Gravel.Abstractions.Storage.Wal;

namespace Gravel.Engine.Managers;

class WalManager
{
    readonly string _walDir;
    readonly IWalFactory _walFactory;

    public WalManager(IWalFactory walFactory, string walDir)
    {
        _walFactory = walFactory;
        _walDir = walDir;
        WalWriter = _walFactory.CreateWriter(_walDir);
    }

    public IWalWriter WalWriter { get; }
    // Add WAL-related methods here
}
