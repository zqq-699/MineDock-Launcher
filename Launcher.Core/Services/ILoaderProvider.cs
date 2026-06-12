using Launcher.Core.Models;

namespace Launcher.Core.Services;

public interface ILoaderProvider
{
    LoaderKind Kind { get; }
    string DisplayName { get; }
    bool IsImplemented { get; }
    Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default);
    Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
}
