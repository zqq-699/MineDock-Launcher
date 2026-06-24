namespace Launcher.Infrastructure.CurseForge;

public interface ICurseForgeApiKeyResolver
{
    Task<string?> TryResolveAsync(CancellationToken cancellationToken = default);
}
