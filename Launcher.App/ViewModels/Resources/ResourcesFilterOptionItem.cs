namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesFilterOptionItem
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<string> MinecraftVersions { get; init; } = [];
}
