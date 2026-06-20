using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Fakes;

internal sealed class FakeGameVersionService : IGameVersionService
{
    private readonly IReadOnlyList<MinecraftVersionInfo> versions;

    public FakeGameVersionService(IReadOnlyList<MinecraftVersionInfo> versions)
    {
        this.versions = versions;
    }

    public int CallCount { get; private set; }

    public DownloadSourcePreference LastDownloadSourcePreference { get; private set; } = DownloadSourcePreference.Auto;
    public int LastDownloadSpeedLimitMbPerSecond { get; private set; }

    public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        CallCount++;
        LastDownloadSourcePreference = downloadSourcePreference;
        LastDownloadSpeedLimitMbPerSecond = downloadSpeedLimitMbPerSecond;
        return Task.FromResult(versions);
    }
}
