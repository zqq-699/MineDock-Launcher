namespace Launcher.Application.Services;

public sealed record LauncherUpdateInfo(
    string Version,
    string DisplayVersion,
    string ReleasePageUrl,
    string? DownloadUrl,
    string? Changelog,
    string? DownloadFileName = null,
    LauncherUpdateAssetKind AssetKind = LauncherUpdateAssetKind.ReleasePage)
{
    public bool CanAutoInstall => AssetKind is LauncherUpdateAssetKind.WindowsX64Executable
        && !string.IsNullOrWhiteSpace(DownloadUrl);
}
