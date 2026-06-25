namespace Launcher.Application.Services;

public interface IModpackWorkspaceCleanupService
{
    Task CleanupAllAsync(CancellationToken cancellationToken = default);
}
