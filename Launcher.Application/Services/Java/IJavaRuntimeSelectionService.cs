using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IJavaRuntimeSelectionService
{
    Task<JavaRuntimeInfo> SelectForLaunchAsync(
        GameInstance instance,
        LauncherSettings settings,
        CancellationToken cancellationToken = default);
}
