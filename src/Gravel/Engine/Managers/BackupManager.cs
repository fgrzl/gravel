using System.IO.Compression;
using System.Text.Json;
using Gravel.Abstractions.Storage.Wal;
using Gravel.Exceptions;
using Microsoft.Extensions.Logging;

namespace Gravel.Engine.Managers;

sealed class BackupManager(IWalWriter walWriter, Levels levels, string sstDir, string walDir, ILogger logger)
{
    readonly Levels _levels = levels ?? throw new ArgumentNullException(nameof(levels));
    readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    readonly string _sstDir = sstDir ?? throw new ArgumentNullException(nameof(sstDir));
    readonly string _walDir = walDir ?? throw new ArgumentNullException(nameof(walDir));
    readonly IWalWriter _walWriter = walWriter ?? throw new ArgumentNullException(nameof(walWriter));

    public async ValueTask BackupAsync(
        string destinationPath,
        BackupOptions? options,
        SemaphoreSlim commitGate,
        Func<CancellationToken, ValueTask> flushMemTableAsync,
        CancellationToken ct = default)
    {
        options ??= new BackupOptions();

        // Short snapshot window: prevent commits while we capture lastSequence and level snapshot
        await commitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (options.ForceMemTableFlush)
                // Force flush memtable to SST so backup includes all in-memory data
                await flushMemTableAsync(ct).ConfigureAwait(false);

            // Ensure WAL is durable
            try
            {
                await _walWriter.FlushAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                /* best-effort */
            }

            var lastSeq = _walWriter.LastSequence;
            var sstSnapshot = _levels.SnapshotAll();

            // Build manifest
            var manifest = new
            {
                formatVersion = 1,
                createdAt = DateTimeOffset.UtcNow,
                lastSequence = lastSeq,
                sst = sstSnapshot.Select(f => new { path = f.Path, seq = f.SequenceTag, level = 0 }).ToList(),
                wal = options.IncludeWalSegments ? new List<object>() : null
            };

            // Create zip archive (manifest + files)
            await using var fs = File.Create(destinationPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false);
            // Write manifest.json entry
            var manifestBytes =
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
            var mEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var mStream = mEntry.Open())
            {
                await mStream.WriteAsync(manifestBytes, ct).ConfigureAwait(false);
            }

            // Add SST files
            foreach (var f in sstSnapshot)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(f.Path)) continue;

                // Preserve the path relative to the configured sst directory so restore places files under L{n}/ etc.
                string relativePath;
                try
                {
                    relativePath = Path.GetRelativePath(_sstDir, f.Path);
                }
                catch
                {
                    // Fallback to filename only if relative path computation fails
                    relativePath = Path.GetFileName(f.Path);
                }

                var entryName = Path.Combine("sst", relativePath).Replace('\\', '/');
                var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var es = e.Open();
                await using var src = File.OpenRead(f.Path);
                await src.CopyToAsync(es, ct).ConfigureAwait(false);
            }

            // Add WAL files (if requested)
            if (options.IncludeWalSegments)
            {
                var walFiles = Directory.Exists(_walDir) ? Directory.GetFiles(_walDir) : [];
                foreach (var wf in walFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var entryName = Path.Combine("wal", Path.GetFileName(wf)).Replace('\\', '/');
                    var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var es = e.Open();
                    await using var src = File.OpenRead(wf);
                    await src.CopyToAsync(es, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            commitGate.Release();
        }
    }

    public async ValueTask RestoreAsync(
        string archivePath,
        RestoreOptions? options,
        string databasePath,
        CancellationToken ct = default)
    {
        options ??= new RestoreOptions();

        // Extract to temp directory
        var tmp = Path.Combine(Path.GetTempPath(), $"gravel_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);

        using var zf = ZipFile.OpenRead(archivePath);
        foreach (var entry in zf.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(tmp, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(dest)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (entry.FullName.EndsWith("/"))
            {
                if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
                continue;
            }

            await using var inStream = entry.Open();
            await using var outFs = File.Create(dest);
            await inStream.CopyToAsync(outFs, ct).ConfigureAwait(false);
        }

        // Optionally verify manifest/checksums (not implemented: basic presence check)
        var manifestPath = Path.Combine(tmp, "manifest.json");
        if (!File.Exists(manifestPath)) throw new GravelInvalidOperationException("Backup manifest missing");

        // Move extracted tree into target database directory (atomic replacement)
        var targetBase = databasePath;
        var backupOld = targetBase + ".bak_old_" + Guid.NewGuid().ToString("N");

        if (Directory.Exists(targetBase)) Directory.Move(targetBase, backupOld);

        Directory.Move(tmp, targetBase);

        // cleanup old db if needed (best-effort)
        try
        {
            if (Directory.Exists(backupOld)) Directory.Delete(backupOld, true);
        }
        catch
        {
        }
    }
}
