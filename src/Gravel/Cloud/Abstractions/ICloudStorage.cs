namespace Gravel.Cloud.Abstractions;

/// <summary>
///     Abstraction for cloud object storage (Azure Blob Storage, S3, Wasabi, etc.).
///     Enables pluggable cloud backends for WAL and SST persistence.
/// </summary>
public interface ICloudStorage : IAsyncDisposable
{
    /// <summary>
    ///     Checks if an object exists at the given path.
    /// </summary>
    /// <param name="path">The object path/key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True if the object exists; otherwise, false.</returns>
    ValueTask<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Uploads data from a stream to cloud storage.
    /// </summary>
    /// <param name="path">The target object path/key.</param>
    /// <param name="stream">The source stream to upload from.</param>
    /// <param name="metadata">Optional metadata to attach (varies by backend).</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask UploadAsync(
        string path,
        Stream stream,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Downloads an object from cloud storage to a stream.
    /// </summary>
    /// <param name="path">The source object path/key.</param>
    /// <param name="stream">The target stream to download to.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask DownloadAsync(string path, Stream stream, CancellationToken ct = default);

    /// <summary>
    ///     Lists objects at a given path prefix.
    /// </summary>
    /// <param name="prefix">The path prefix to search.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of object paths.</returns>
    IAsyncEnumerable<string> ListAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    ///     Deletes an object from cloud storage.
    /// </summary>
    /// <param name="path">The object path/key to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    ///     Gets the size of an object in bytes.
    /// </summary>
    /// <param name="path">The object path/key.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The size in bytes, or null if the object doesn't exist.</returns>
    ValueTask<long?> GetSizeAsync(string path, CancellationToken ct = default);
}
