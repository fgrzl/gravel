using Gravel.Abstractions.Storage.Sst;

namespace Gravel.Engine.Managers;

class SstManager(ISstFactory sstFactory, string sstDir, Levels levels)
{
    readonly Levels _levels = levels;
    readonly string _sstDir = sstDir;

    readonly ISstFactory _sstFactory = sstFactory;
    // Add SST-related methods here
}
