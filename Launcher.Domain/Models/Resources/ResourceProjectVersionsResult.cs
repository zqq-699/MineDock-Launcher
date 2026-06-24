namespace Launcher.Domain.Models;

public sealed class ResourceProjectVersionsResult
{
    public IReadOnlyList<ResourceProjectVersion> Versions { get; init; } = [];

    public bool IsCurseForgeUnavailable { get; init; }

    public bool IsCurseForgeApiKeyMissing { get; init; }

    public bool HasMore { get; init; }
}
