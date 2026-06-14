using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IGameVersionService
{
    Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken = default);
}
