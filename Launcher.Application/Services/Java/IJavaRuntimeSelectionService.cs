using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IJavaRuntimeSelectionService
{
    Task<JavaRuntimeInfo> SelectForLaunchAsync(
        GameInstance instance,
        LauncherSettings settings,
        LaunchRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}
