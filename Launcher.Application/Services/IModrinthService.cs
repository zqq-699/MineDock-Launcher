using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModrinthService
{
    Task<IReadOnlyList<ModrinthProject>> SearchModsAsync(string query, string minecraftVersion, LoaderKind loader, CancellationToken cancellationToken = default);
    Task<string> InstallLatestCompatibleAsync(ModrinthProject project, GameInstance instance, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
}
