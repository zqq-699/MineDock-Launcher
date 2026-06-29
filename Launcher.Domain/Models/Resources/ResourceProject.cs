namespace Launcher.Domain.Models;

public sealed class ResourceProject
{
    public ResourceProjectKind Kind { get; init; } = ResourceProjectKind.Mod;

    public ResourceProjectSource Source { get; init; }

    public string ProjectId { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? IconUrl { get; init; }

    public long Downloads { get; init; }

    public IReadOnlyList<string> SupportedMinecraftVersions { get; init; } = [];

    public IReadOnlyList<string> SupportedLoaders { get; init; } = [];

    public string ProjectUrl { get; init; } = string.Empty;
}
