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

    public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(versions);
    }
}

