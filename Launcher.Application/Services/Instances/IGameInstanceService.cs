using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IGameInstanceService
{
    Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default);
    Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default);
    Task<GameInstance> CreateInstanceAsync(string minecraftVersion, LoaderKind loader, string? loaderVersion, string? name, IProgress<LauncherProgress>? progress, CancellationToken cancellationToken = default);
    Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default);
    Task<GameInstance> RenameInstanceAsync(string instanceId, string? newName, string? newIconSource, CancellationToken cancellationToken = default);
    Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default);
    Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default);
}
