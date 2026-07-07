namespace Launcher.Application.Services;

public interface ILauncherUpdateService
{
    Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        CancellationToken cancellationToken = default);
}
