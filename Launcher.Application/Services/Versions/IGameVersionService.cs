using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IGameVersionService
{
    Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0);
}
