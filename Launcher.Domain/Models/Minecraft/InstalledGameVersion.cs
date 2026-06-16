namespace Launcher.Domain.Models;

public sealed record InstalledGameVersion(
    string VersionName,
    string MinecraftVersion,
    string VersionType,
    LoaderKind Loader,
    string? LoaderVersion,
    string Directory,
    DateTimeOffset DiscoveredAt);
