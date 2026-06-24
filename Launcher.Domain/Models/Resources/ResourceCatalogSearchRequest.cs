namespace Launcher.Domain.Models;

public sealed class ResourceCatalogSearchRequest
{
    public string Query { get; init; } = string.Empty;

    public string MinecraftVersion { get; init; } = string.Empty;

    public LoaderKind Loader { get; init; } = LoaderKind.Vanilla;

    public ResourceProjectSource? Source { get; init; }
}
