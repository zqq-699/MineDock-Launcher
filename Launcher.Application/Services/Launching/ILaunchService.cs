using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILaunchService
{
    Task<GameLaunchSession> LaunchAsync(
        GameInstance instance,
        LauncherAccount account,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        LaunchRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}
