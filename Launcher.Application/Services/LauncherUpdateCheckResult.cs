namespace Launcher.Application.Services;

public sealed record LauncherUpdateCheckResult(
    bool IsUpdateAvailable,
    LauncherUpdateInfo? Update,
    string CurrentVersion,
    bool IsFailed,
    string? ErrorMessage)
{
    public static LauncherUpdateCheckResult Latest(string currentVersion)
    {
        return new LauncherUpdateCheckResult(false, null, currentVersion, false, null);
    }

    public static LauncherUpdateCheckResult Available(string currentVersion, LauncherUpdateInfo update)
    {
        return new LauncherUpdateCheckResult(true, update, currentVersion, false, null);
    }

    public static LauncherUpdateCheckResult Failed(string currentVersion, string? errorMessage = null)
    {
        return new LauncherUpdateCheckResult(false, null, currentVersion, true, errorMessage);
    }
}
