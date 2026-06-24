namespace Launcher.Domain.Models;

public sealed class ResourceProjectVersionsRequest
{
    public ResourceProjectSource Source { get; init; }

    public string ProjectId { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string MinecraftVersion { get; init; } = string.Empty;

    public LoaderKind Loader { get; init; } = LoaderKind.Vanilla;

    public bool IncludeAllVersions { get; init; }

    public int Offset { get; init; }

    public int PageSize { get; init; } = 50;
}
