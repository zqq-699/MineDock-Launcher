namespace Launcher.Domain.Models;

public sealed class ResourceCatalogSearchRequest
{
    public string Query { get; init; } = string.Empty;

    public string MinecraftVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> MinecraftVersions { get; init; } = [];

    public LoaderKind Loader { get; init; } = LoaderKind.Vanilla;

    public ResourceProjectSource? Source { get; init; }

    public ResourceProjectCategory? Category { get; init; }

    public int Offset { get; init; }

    public int PageSize { get; init; } = 20;
}
