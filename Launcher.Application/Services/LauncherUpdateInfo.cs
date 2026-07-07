namespace Launcher.Application.Services;

public sealed record LauncherUpdateInfo(
    string Version,
    string DisplayVersion,
    string ReleasePageUrl,
    string? DownloadUrl,
    string? Changelog);
