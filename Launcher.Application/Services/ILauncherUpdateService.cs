using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILauncherUpdateService
{
    Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
        string currentVersion,
        LauncherUpdateChannel channel,
        CancellationToken cancellationToken = default);
}
