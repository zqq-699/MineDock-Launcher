namespace Launcher.Application.Accounts;

public interface ILaunchAccountSessionService
{
    Task<LaunchAccountSession> CreateSessionAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default);
}
