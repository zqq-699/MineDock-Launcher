namespace Launcher.Domain.Models;

public sealed class ModrinthVersionInfo
{
    public string VersionId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string VersionNumber { get; init; } = string.Empty;

    public bool IsStable { get; init; }
}
