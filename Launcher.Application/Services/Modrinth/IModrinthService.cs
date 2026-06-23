using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModrinthService
{
    Task<IReadOnlyList<ModrinthProject>> SearchModsAsync(string query, string minecraftVersion, LoaderKind loader, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModrinthVersionInfo>> GetQuiltStandardLibraryVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default);
    Task<string> InstallLatestCompatibleAsync(ModrinthProject project, GameInstance instance, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
    Task<string> InstallFabricApiAsync(GameInstance instance, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
    Task<string> InstallQuiltStandardLibraryAsync(GameInstance instance, string versionId, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
}
