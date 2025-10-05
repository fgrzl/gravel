using System;
using System.IO;
using System.Threading;

namespace Gravel.TestHelpers;

/// <summary>
///     Creates a unique temporary directory and removes it on Dispose.
///     Uses best-effort retries to work around transient file locks on CI.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix)
    {
        prefix ??= "tmp-";
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            // retry deletion a few times to avoid transient lock failures on CI
            for (var i = 0; i < 5; i++)
                try
                {
                    Directory.Delete(Path, true);
                    break;
                }
                catch
                {
                    Thread.Sleep(50);
                }
    }
}
