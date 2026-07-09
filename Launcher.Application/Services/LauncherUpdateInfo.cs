namespace Launcher.Application.Services;

public sealed record LauncherUpdateDownloadUrl(
    string Name,
    string Url,
    int Priority);

public sealed record LauncherUpdateInfo(
    string Version,
    string DisplayVersion,
    string ReleasePageUrl,
    string? DownloadUrl,
    string? Changelog,
    string? DownloadFileName = null,
    LauncherUpdateAssetKind AssetKind = LauncherUpdateAssetKind.ReleasePage,
    int VersionCode = 0,
    bool IsMandatory = false,
    int MinSupportedVersionCode = 0,
    DateTimeOffset? PublishedAt = null,
    long SizeBytes = 0,
    string? Sha256 = null,
    IReadOnlyList<LauncherUpdateDownloadUrl>? DownloadUrls = null)
{
    public bool CanAutoInstall => AssetKind is LauncherUpdateAssetKind.WindowsX64Executable
        && EffectiveDownloadUrls.Count > 0;

    public IReadOnlyList<LauncherUpdateDownloadUrl> EffectiveDownloadUrls =>
        DownloadUrls is { Count: > 0 }
            ? DownloadUrls
            : string.IsNullOrWhiteSpace(DownloadUrl)
                ? []
                : [new LauncherUpdateDownloadUrl("default", DownloadUrl, 1)];
}
