using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILaunchService
{
    Task LaunchAsync(GameInstance instance, LauncherSettings settings, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
}
