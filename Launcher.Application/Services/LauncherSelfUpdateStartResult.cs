namespace Launcher.Application.Services;

public sealed record LauncherSelfUpdateStartResult(
    bool Succeeded,
    string? DownloadedFilePath,
    string? ErrorMessage)
{
    public static LauncherSelfUpdateStartResult Success(string downloadedFilePath)
    {
        return new LauncherSelfUpdateStartResult(true, downloadedFilePath, null);
    }

    public static LauncherSelfUpdateStartResult Failed(string? errorMessage = null)
    {
        return new LauncherSelfUpdateStartResult(false, null, errorMessage);
    }
}
