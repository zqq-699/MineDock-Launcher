using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IJavaRuntimeProvisioningService
{
    Task EnsureForLaunchAsync(
        GameInstance instance,
        LauncherSettings settings,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default);
}
