using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

public sealed class LocalModpackImportService : ILocalModpackImportService
{
    private readonly IGameInstanceService instanceService;
    private readonly IModpackPackageService modpackPackageService;
    private readonly ILogger<LocalModpackImportService> logger;

    public LocalModpackImportService(
        IGameInstanceService instanceService,
        IModpackPackageService modpackPackageService,
        ILogger<LocalModpackImportService>? logger = null)
    {
        this.instanceService = instanceService;
        this.modpackPackageService = modpackPackageService;
        this.logger = logger ?? NullLogger<LocalModpackImportService>.Instance;
    }

    public async Task<ModpackRecognitionResult> RecognizeArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await modpackPackageService
                .RecognizeAsync(archivePath, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Modpack archive recognition completed. ArchivePath={ArchivePath} IsSuccess={IsSuccess} FailureReason={FailureReason}",
                archivePath,
                result.IsSuccess,
                result.FailureReason);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected modpack archive recognition failure. ArchivePath={ArchivePath}",
                archivePath);
            return ModpackRecognitionResult.Failure(ModpackRecognitionFailureReason.UnexpectedError);
        }
    }

    public async Task<ModpackImportResult> ImportFromArchiveAsync(
        string archivePath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        PreparedModpack? preparedModpack = null;
        GameInstance? createdInstance = null;

        try
        {
            progress?.Report(new LauncherProgress(ImportProgressStages.PreparingArchive, string.Empty));
            progress?.Report(new LauncherProgress(ImportProgressStages.ParsingManifest, string.Empty));
            preparedModpack = await modpackPackageService.PrepareAsync(archivePath, cancellationToken).ConfigureAwait(false);
            var resolvedInstanceName = await ResolveUniqueInstanceNameAsync(
                preparedModpack.PackageName,
                cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Importing modpack. ArchivePath={ArchivePath} ModpackName={ModpackName} MinecraftVersion={MinecraftVersion} Loader={Loader} LoaderVersion={LoaderVersion} ResolvedInstanceName={ResolvedInstanceName}",
                archivePath,
                preparedModpack.PackageName,
                preparedModpack.MinecraftVersion,
                preparedModpack.Loader,
                preparedModpack.LoaderVersion,
                resolvedInstanceName);

            progress?.Report(new LauncherProgress(ImportProgressStages.CreatingInstance, string.Empty));
            createdInstance = await instanceService.CreateInstanceAsync(
                preparedModpack.MinecraftVersion,
                preparedModpack.Loader,
                preparedModpack.LoaderVersion,
                resolvedInstanceName,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                installFabricApi: false).ConfigureAwait(false);

            await modpackPackageService.InstallContentAsync(
                preparedModpack,
                createdInstance,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false);

            try
            {
                await modpackPackageService.CleanupAsync(preparedModpack, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to clean up modpack workspace after a successful import. WorkingDirectory={WorkingDirectory}",
                    preparedModpack.WorkingDirectory);
            }

            logger.LogInformation(
                "Modpack import completed. ArchivePath={ArchivePath} InstanceId={InstanceId} InstanceName={InstanceName}",
                archivePath,
                createdInstance.Id,
                createdInstance.Name);
            return ModpackImportResult.Success(createdInstance);
        }
        catch (ModpackImportException exception)
        {
            logger.LogWarning(
                exception,
                "Modpack import failed with a known reason. ArchivePath={ArchivePath} FailureReason={FailureReason}",
                archivePath,
                exception.FailureReason);
            await CleanupFailedImportAsync(preparedModpack, createdInstance, progress, cancellationToken).ConfigureAwait(false);
            return ModpackImportResult.Failure(exception.FailureReason);
        }
        catch (OperationCanceledException)
        {
            await CleanupFailedImportAsync(preparedModpack, createdInstance, progress, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected modpack import failure. ArchivePath={ArchivePath}",
                archivePath);
            await CleanupFailedImportAsync(preparedModpack, createdInstance, progress, cancellationToken).ConfigureAwait(false);
            return ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError);
        }
    }

    private async Task CleanupFailedImportAsync(
        PreparedModpack? preparedModpack,
        GameInstance? createdInstance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (createdInstance is not null || preparedModpack is not null)
            progress?.Report(new LauncherProgress(ImportProgressStages.CleaningUp, string.Empty));

        if (createdInstance is not null)
        {
            try
            {
                await instanceService.DeleteInstanceAsync(createdInstance.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete partially imported instance. InstanceId={InstanceId}",
                    createdInstance.Id);
            }
        }

        if (preparedModpack is null)
            return;

        try
        {
            await modpackPackageService.CleanupAsync(preparedModpack, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to clean up prepared modpack workspace. WorkingDirectory={WorkingDirectory}",
                preparedModpack.WorkingDirectory);
        }
    }

    private async Task<string> ResolveUniqueInstanceNameAsync(string preferredName, CancellationToken cancellationToken)
    {
        var instances = await instanceService.GetInstancesAsync(cancellationToken).ConfigureAwait(false);
        var unavailableNames = new HashSet<string>(
            instances
                .SelectMany(instance => new[] { instance.Name, instance.VersionName })
                .Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        var baseName = preferredName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseName))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Prepared modpack package name is missing.");
        }

        if (!unavailableNames.Contains(baseName))
            return baseName;

        var suffix = 1;
        while (true)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!unavailableNames.Contains(candidate))
                return candidate;

            suffix++;
        }
    }
}
