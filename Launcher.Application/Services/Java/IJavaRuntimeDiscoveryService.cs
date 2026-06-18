using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IJavaRuntimeDiscoveryService
{
    Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
        string? minecraftDirectory,
        CancellationToken cancellationToken = default);

    Task<JavaRuntimeInfo> DiscoverExecutableAsync(
        string executablePath,
        CancellationToken cancellationToken = default);
}
