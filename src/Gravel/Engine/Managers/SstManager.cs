using Gravel.Abstractions.Storage.Sst;
using Gravel.Logging;
using Microsoft.Extensions.Logging;

namespace Gravel.Engine.Managers;

sealed class SstManager(ISstFactory sstFactory, Levels levels, string sstDir, ILogger logger)
{
    readonly Levels _levels = levels ?? throw new ArgumentNullException(nameof(levels));
    readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    readonly string _sstDir = sstDir ?? throw new ArgumentNullException(nameof(sstDir));
    readonly ISstFactory _sstFactory = sstFactory ?? throw new ArgumentNullException(nameof(sstFactory));

    public async Task LoadExistingAsync(CancellationToken ct = default)
    {
        for (var l = 0; ; l++)
        {
            if (l >= _levels.LevelCount) break;
            var files = _sstFactory.EnumerateLevelFiles(_sstDir, l);
            foreach (var f in files)
            {
                try
                {
                    var r = await _sstFactory.CreateReaderAsync(f, ct).ConfigureAwait(false);
                    _levels.Add(l, new SstFile(f, r, 0));
                    Log.SstLoaded(_logger, l, f);
                }
                catch (Exception ex)
                {
                    Log.SstLoadFailed(_logger, f, ex.Message);
                }
            }
        }
    }

    public async ValueTask<SstFile> WriteMemTableAsync(MemTable mt, ulong seqTag, CancellationToken ct = default)
    {
        var dir = Path.Combine(_sstDir, "L0");
        var path = Path.Combine(dir, $"{seqTag:D20}.sst");

        await using (var w = await _sstFactory.CreateWriterAsync(path, mt.Count, ct).ConfigureAwait(false))
        {
            await w.WriteAsync(mt.EnumerateEntriesAsync(ct), ct).ConfigureAwait(false);
        }

        var r = await _sstFactory.CreateReaderAsync(path, ct).ConfigureAwait(false);
        var sst = new SstFile(path, r, seqTag);
        _levels.Add(0, sst);

        Log.SstCreated(_logger, path);
        Log.FlushComplete(_logger, path, seqTag);

        return sst;
    }

    public ValueTask<ISstReader> CreateReaderAsync(string path, CancellationToken ct = default)
    {
        return _sstFactory.CreateReaderAsync(path, ct);
    }
}
