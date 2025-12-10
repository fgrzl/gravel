using System.Runtime.CompilerServices;
using Gravel.Cloud.Abstractions;

namespace Gravel.Cloud.NoOp;

/// <summary>
///     No-op implementation of cloud storage. Useful for local-only deployments
///     or testing. All operations succeed but don't persist to any backend.
/// </summary>
public sealed class NoOpCloudStorage : ICloudStorage
{
    /// <summary>
    ///     Checks if an object exists (always returns false in no-op implementation).
    /// </summary>
    public ValueTask<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        return ValueTask.FromResult(false);
    }

    /// <summary>
    ///     Uploads data (no-op, just consumes the stream).
    /// </summary>
    public ValueTask UploadAsync(
        string path,
        Stream stream,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        // Consume stream but don't store
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Downloads data (always fails in no-op implementation).
    /// </summary>
    public ValueTask DownloadAsync(string path, Stream stream, CancellationToken ct = default)
    {
        throw new FileNotFoundException($"No-op cloud storage has no object: {path}");
    }

    /// <summary>
    ///     Lists objects (returns empty enumerable in no-op implementation).
    /// </summary>
    public async IAsyncEnumerable<string> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Return empty enumerable
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    ///     Deletes an object (no-op).
    /// </summary>
    public ValueTask DeleteAsync(string path, CancellationToken ct = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Gets object size (always returns null in no-op implementation).
    /// </summary>
    public ValueTask<long?> GetSizeAsync(string path, CancellationToken ct = default)
    {
        return ValueTask.FromResult<long?>(null);
    }

    /// <summary>
    ///     Disposes the storage.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
