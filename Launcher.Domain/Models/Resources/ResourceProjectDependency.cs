namespace Launcher.Domain.Models;

public sealed class ResourceProjectDependency
{
    public ResourceProject Project { get; init; } = new();

    public string VersionId { get; init; } = string.Empty;
}
