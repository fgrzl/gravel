namespace Gravel.Engine;

public sealed class RestoreOptions
{
    public bool VerifyChecksums { get; set; } = true;
    public bool RequireEngineStopped { get; set; } = true;
}
