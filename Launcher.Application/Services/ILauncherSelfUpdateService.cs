namespace Launcher.Application.Services;

public interface ILauncherSelfUpdateService
{
    Task<LauncherSelfUpdateStartResult> StartUpdateAsync(
        LauncherUpdateInfo update,
        CancellationToken cancellationToken = default);
}
