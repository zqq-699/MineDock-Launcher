namespace Launcher.Domain.Models;

public sealed class ResourceCatalogSearchResult
{
    public IReadOnlyList<ResourceProject> Projects { get; init; } = [];

    public bool IsCurseForgeUnavailable { get; init; }

    public bool IsCurseForgeApiKeyMissing { get; init; }
}
