using Launcher.Core.Models;

namespace Launcher.Core.Services;

public interface ILaunchService
{
    Task LaunchAsync(GameInstance instance, LauncherSettings settings, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
}
