using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ILoaderProvider
{
    LoaderKind Kind { get; }
    string DisplayName { get; }
    bool IsImplemented { get; }
    Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default);
    Task<string> InstallAsync(string minecraftVersion, string gameDirectory, string isolatedVersionName, string? loaderVersion, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
}
