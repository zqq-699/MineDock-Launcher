using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModpackGameInstaller
{
    Task InstallMinecraftBaseAsync(
        string minecraftVersion,
        string gameDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);

    Task<string> InstallLoaderAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string gameDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);

    Task<string> InstallInstanceAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string gameDirectory,
        string isolatedVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);
}
