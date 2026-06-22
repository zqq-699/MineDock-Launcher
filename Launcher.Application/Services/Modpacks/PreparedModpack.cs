using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public enum ModpackPackageKind
{
    Modrinth,
    CurseForge
}

public sealed class PreparedModpack
{
    public ModpackPackageKind PackageKind { get; init; }

    public string SourceArchivePath { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string PackageName { get; init; } = string.Empty;

    public string MinecraftVersion { get; init; } = string.Empty;

    public LoaderKind Loader { get; init; } = LoaderKind.Vanilla;

    public string? LoaderVersion { get; init; }

    public string? OverridesDirectory { get; init; }

    public IReadOnlyList<PreparedModpackDownload> Files { get; init; } = [];

    public IReadOnlyList<ManualModpackDownload> ManualDownloads { get; set; } = [];

    public string? ManualDownloadsFilePath { get; set; }
}

public sealed class PreparedModpackDownload
{
    public string FileName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string TargetDirectory { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public long? ProjectId { get; init; }

    public long? FileId { get; init; }

    public string? Sha1 { get; init; }

    public string? Sha512 { get; init; }
}
