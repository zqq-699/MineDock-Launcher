namespace Launcher.Domain.Models;

public sealed class ResourceProjectDependenciesResult
{
    public IReadOnlyList<ResourceProject> RequiredProjects { get; init; } = [];
}
