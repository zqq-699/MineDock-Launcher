using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IGameInstallCoordinator
{
    ValueTask<IAsyncDisposable> AcquireInstallAsync(
        string minecraftDirectory,
        string versionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default);

    bool IsInstallingVersion(string minecraftDirectory, string versionName);
}
