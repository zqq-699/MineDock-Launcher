using Launcher.Core.Models;

namespace Launcher.Core.Services;

public interface IGameVersionService
{
    Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken = default);
}
