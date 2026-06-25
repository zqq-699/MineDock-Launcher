using System.IO;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class ModpackWorkspaceCleanupService : IModpackWorkspaceCleanupService
{
    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<ModpackWorkspaceCleanupService> logger;

    public ModpackWorkspaceCleanupService(
        LauncherPathProvider pathProvider,
        ILogger<ModpackWorkspaceCleanupService>? logger = null)
    {
        this.pathProvider = pathProvider;
        this.logger = logger ?? NullLogger<ModpackWorkspaceCleanupService>.Instance;
    }

    public Task CleanupAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CleanupAll(cancellationToken), cancellationToken);
    }

    private void CleanupAll(CancellationToken cancellationToken)
    {
        var modpackCacheDirectory = Path.Combine(pathProvider.DefaultDataDirectory, "cache", "modpacks");
        logger.LogInformation(
            "Cleaning modpack workspace cache. CacheDirectory={CacheDirectory}",
            modpackCacheDirectory);

        if (!Directory.Exists(modpackCacheDirectory))
        {
            logger.LogInformation(
                "Modpack workspace cache cleanup completed. CacheDirectory={CacheDirectory} DeletedCount={DeletedCount} FailedCount={FailedCount}",
                modpackCacheDirectory,
                0,
                0);
            return;
        }

        var deletedCount = 0;
        var failedCount = 0;

        foreach (var workspaceDirectory in Directory.EnumerateDirectories(modpackCacheDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Directory.Delete(workspaceDirectory, recursive: true);
                deletedCount++;
            }
            catch (IOException exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete modpack workspace cache directory. Directory={Directory}",
                    workspaceDirectory);
            }
            catch (UnauthorizedAccessException exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete modpack workspace cache directory. Directory={Directory}",
                    workspaceDirectory);
            }
        }

        logger.LogInformation(
            "Modpack workspace cache cleanup completed. CacheDirectory={CacheDirectory} DeletedCount={DeletedCount} FailedCount={FailedCount}",
            modpackCacheDirectory,
            deletedCount,
            failedCount);
    }
}
