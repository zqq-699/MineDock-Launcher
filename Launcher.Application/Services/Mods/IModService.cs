using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModService
{
    Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default);
    Task<LocalMod> ImportAsync(GameInstance instance, string sourceJarPath, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default);
    Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default);
}
