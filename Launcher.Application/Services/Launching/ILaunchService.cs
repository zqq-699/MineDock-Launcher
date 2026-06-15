using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILaunchService
{
    Task LaunchAsync(
        GameInstance instance,
        LauncherAccount account,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default);
}
