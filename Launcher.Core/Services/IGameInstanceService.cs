using Launcher.Core.Models;

namespace Launcher.Core.Services;

public interface IGameInstanceService
{
    Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default);
    Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default);
    Task<GameInstance> CreateInstanceAsync(string minecraftVersion, LoaderKind loader, string? loaderVersion, string? name, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
    Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default);
}
