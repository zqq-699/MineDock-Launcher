namespace Launcher.Domain.Models;

public sealed record LauncherProgress(
    string Stage,
    string Message,
    double? Percent = null,
    string? DownloadSpeedText = null);
