namespace Launcher.Domain.Models;

public sealed class ResourceProjectDependenciesRequest
{
    public ResourceProjectKind Kind { get; init; } = ResourceProjectKind.Mod;

    public ResourceProjectSource Source { get; init; }

    public string ProjectId { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;
}
