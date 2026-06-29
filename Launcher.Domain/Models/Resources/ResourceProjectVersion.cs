namespace Launcher.Domain.Models;

public sealed class ResourceProjectVersion
{
    public ResourceProjectKind Kind { get; init; } = ResourceProjectKind.Mod;

    public string VersionId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string VersionNumber { get; init; } = string.Empty;

    public string VersionType { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string PrimaryDownloadUrl { get; init; } = string.Empty;

    public IReadOnlyList<string> FallbackDownloadUrls { get; init; } = [];

    public long Downloads { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public IReadOnlyList<string> GameVersions { get; init; } = [];

    public IReadOnlyList<string> Loaders { get; init; } = [];

    public IReadOnlyList<ResourceProjectDependency> RequiredDependencies { get; init; } = [];
}
