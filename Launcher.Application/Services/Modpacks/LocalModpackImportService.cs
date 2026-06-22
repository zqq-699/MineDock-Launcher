using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

public sealed class LocalModpackImportService : ILocalModpackImportService
{
    private readonly IGameInstanceService instanceService;
    private readonly IModpackPackageService modpackPackageService;
    private readonly IModpackGameInstaller modpackGameInstaller;
    private readonly IModpackInstanceStagingService stagingService;
    private readonly ILogger<LocalModpackImportService> logger;

    public LocalModpackImportService(
        IGameInstanceService instanceService,
        IModpackPackageService modpackPackageService,
        IModpackGameInstaller modpackGameInstaller,
        IModpackInstanceStagingService stagingService,
        ILogger<LocalModpackImportService>? logger = null)
    {
        this.instanceService = instanceService;
        this.modpackPackageService = modpackPackageService;
        this.modpackGameInstaller = modpackGameInstaller;
        this.stagingService = stagingService;
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
        StagedModpackInstance? stagedInstance = null;
        GameInstance? importedInstance = null;
        string? finalVersionName = null;
        var importProgress = progress is null ? null : new OverallModpackImportProgress(progress);

        try
        {
            importProgress?.Report(new LauncherProgress(ImportProgressStages.PreparingArchive, string.Empty));
            importProgress?.Report(new LauncherProgress(ImportProgressStages.ParsingManifest, string.Empty));
            preparedModpack = await modpackPackageService
                .PrepareAsync(archivePath, cancellationToken, importProgress)
                .ConfigureAwait(false);
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

            importProgress?.Report(new LauncherProgress(ImportProgressStages.CreatingInstance, string.Empty));
            stagedInstance = await stagingService
                .StageAsync(preparedModpack, resolvedInstanceName, cancellationToken)
                .ConfigureAwait(false);
            importProgress?.Report(new LauncherProgress(ImportProgressStages.CreatingInstance, string.Empty, 100));

            var minecraftBaseTask = modpackGameInstaller.InstallMinecraftBaseAsync(
                preparedModpack.MinecraftVersion,
                stagedInstance.MinecraftDirectory,
                CreateInstallStageProgress(importProgress, ImportProgressStages.InstallingMinecraftBase),
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond);
            var loaderInstallTask = InstallLoaderAfterBaseAsync(
                preparedModpack,
                stagedInstance,
                minecraftBaseTask,
                importProgress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond);
            var downloadModsTask = CompleteWithProgressAsync(
                modpackPackageService.DownloadFilesAsync(
                    preparedModpack,
                    stagedInstance.Instance,
                    importProgress,
                    cancellationToken,
                    downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond),
                importProgress,
                new LauncherProgress(ImportProgressStages.DownloadingPackFiles, string.Empty, 100));
            var copyOverridesTask = modpackPackageService.CopyOverridesAsync(
                preparedModpack,
                stagedInstance.Instance,
                importProgress,
                cancellationToken);
            copyOverridesTask = CompleteWithProgressAsync(
                copyOverridesTask,
                importProgress,
                new LauncherProgress(ImportProgressStages.CopyingOverrides, string.Empty, 100));
            var metadataTask = Task.CompletedTask;

            await Task.WhenAll(loaderInstallTask, downloadModsTask, copyOverridesTask, metadataTask).ConfigureAwait(false);

            finalVersionName = await loaderInstallTask.ConfigureAwait(false);
            var manualDownloads = await downloadModsTask.ConfigureAwait(false);
            importedInstance = await stagingService
                .FinalizeAsync(stagedInstance, finalVersionName, cancellationToken)
                .ConfigureAwait(false);

            preparedModpack.ManualDownloads = manualDownloads;
            preparedModpack.ManualDownloadsFilePath = await modpackPackageService
                .WriteManualDownloadsFileAsync(preparedModpack, importedInstance, manualDownloads, cancellationToken)
                .ConfigureAwait(false);

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
                "Modpack import completed. ArchivePath={ArchivePath} InstanceId={InstanceId} InstanceName={InstanceName} ManualDownloadCount={ManualDownloadCount}",
                archivePath,
                importedInstance.Id,
                importedInstance.Name,
                preparedModpack.ManualDownloads.Count);
            return preparedModpack.ManualDownloads.Count > 0
                ? ModpackImportResult.PartialSuccess(importedInstance, preparedModpack.ManualDownloads)
                : ModpackImportResult.Success(importedInstance);
        }
        catch (ModpackImportException exception)
        {
            logger.LogWarning(
                exception,
                "Modpack import failed with a known reason. ArchivePath={ArchivePath} FailureReason={FailureReason}",
                archivePath,
                exception.FailureReason);
            await CleanupFailedImportAsync(preparedModpack, stagedInstance, importedInstance, finalVersionName, importProgress, cancellationToken).ConfigureAwait(false);
            return ModpackImportResult.Failure(exception.FailureReason);
        }
        catch (OperationCanceledException)
        {
            await CleanupFailedImportAsync(preparedModpack, stagedInstance, importedInstance, finalVersionName, importProgress, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected modpack import failure. ArchivePath={ArchivePath}",
                archivePath);
            await CleanupFailedImportAsync(preparedModpack, stagedInstance, importedInstance, finalVersionName, importProgress, cancellationToken).ConfigureAwait(false);
            return ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError);
        }
    }

    private async Task CleanupFailedImportAsync(
        PreparedModpack? preparedModpack,
        StagedModpackInstance? stagedInstance,
        GameInstance? importedInstance,
        string? finalVersionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (stagedInstance is not null || importedInstance is not null || preparedModpack is not null)
            progress?.Report(new LauncherProgress(ImportProgressStages.CleaningUp, string.Empty));

        if (importedInstance is not null)
        {
            try
            {
                await instanceService.DeleteInstanceAsync(importedInstance.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete partially imported instance. InstanceId={InstanceId}",
                    importedInstance.Id);
            }
        }

        if (stagedInstance is not null)
        {
            try
            {
                await stagingService.CleanupFailedImportAsync(stagedInstance, finalVersionName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to clean up staged modpack instance. InstanceName={InstanceName}",
                    stagedInstance.ResolvedInstanceName);
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

    private async Task<string> InstallLoaderAfterBaseAsync(
        PreparedModpack preparedModpack,
        StagedModpackInstance stagedInstance,
        Task minecraftBaseTask,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        await minecraftBaseTask.ConfigureAwait(false);
        return await modpackGameInstaller.InstallLoaderAsync(
            preparedModpack.MinecraftVersion,
            preparedModpack.Loader,
            preparedModpack.LoaderVersion,
            stagedInstance.MinecraftDirectory,
            stagedInstance.ResolvedInstanceName,
            CreateInstallStageProgress(progress, ImportProgressStages.InstallingLoader),
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
    }

    private static IProgress<LauncherProgress>? CreateInstallStageProgress(
        IProgress<LauncherProgress>? innerProgress,
        string installStage)
    {
        if (innerProgress is null)
            return null;

        return new Progress<LauncherProgress>(progress =>
        {
            if (progress.Stage is InstallProgressStages.Preparing)
            {
                innerProgress.Report(progress with { Stage = installStage });
                return;
            }

            innerProgress.Report(progress);
        });
    }

    private static async Task<T> CompleteWithProgressAsync<T>(
        Task<T> task,
        IProgress<LauncherProgress>? progress,
        LauncherProgress completionProgress)
    {
        var result = await task.ConfigureAwait(false);
        progress?.Report(completionProgress);
        return result;
    }

    private static async Task CompleteWithProgressAsync(
        Task task,
        IProgress<LauncherProgress>? progress,
        LauncherProgress completionProgress)
    {
        await task.ConfigureAwait(false);
        progress?.Report(completionProgress);
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

    private sealed class OverallModpackImportProgress(IProgress<LauncherProgress> innerProgress) : IProgress<LauncherProgress>
    {
        private const double ArchiveWeight = 2;
        private const double ManifestWeight = 3;
        private const double InstanceWeight = 5;
        private const double ResolveWeight = 15;
        private const double InstallWeight = 44;
        private const double DownloadWeight = 26;
        private const double OverridesWeight = 4;
        private double lastPercent;
        private double archiveProgress;
        private double manifestProgress;
        private double instanceProgress;
        private double resolveProgress;
        private double installProgress;
        private double downloadProgress;
        private double overridesProgress;

        public void Report(LauncherProgress value)
        {
            var mappedProgress = MapProgress(value);
            innerProgress.Report(mappedProgress);
        }

        private LauncherProgress MapProgress(LauncherProgress value)
        {
            if (!TryUpdateBuckets(value, out var mappedPercent))
                return value;

            var clampedPercent = Math.Clamp(mappedPercent, lastPercent, 99);
            lastPercent = clampedPercent;
            return value with { Percent = clampedPercent };
        }

        private bool TryUpdateBuckets(LauncherProgress value, out double mappedPercent)
        {
            var normalizedPercent = NormalizePercent(value.Percent, treatMissingAsComplete: IsMilestoneStage(value.Stage));
            switch (value.Stage)
            {
                case ImportProgressStages.PreparingArchive:
                    archiveProgress = Math.Max(archiveProgress, normalizedPercent);
                    break;
                case ImportProgressStages.ParsingManifest:
                    archiveProgress = Math.Max(archiveProgress, 1);
                    manifestProgress = Math.Max(manifestProgress, normalizedPercent);
                    break;
                case ImportProgressStages.CreatingInstance:
                    archiveProgress = Math.Max(archiveProgress, 1);
                    manifestProgress = Math.Max(manifestProgress, 1);
                    instanceProgress = Math.Max(instanceProgress, normalizedPercent);
                    break;
                case ImportProgressStages.InstallingMinecraftBase:
                case ImportProgressStages.InstallingLoader:
                    installProgress = Math.Max(installProgress, normalizedPercent);
                    break;
                case ImportProgressStages.ResolvingPackFiles:
                    resolveProgress = Math.Max(resolveProgress, normalizedPercent);
                    break;
                case ImportProgressStages.DownloadingPackFiles:
                    downloadProgress = Math.Max(downloadProgress, normalizedPercent);
                    break;
                case ImportProgressStages.CopyingOverrides:
                    overridesProgress = Math.Max(overridesProgress, normalizedPercent);
                    break;
                case ImportProgressStages.CleaningUp:
                    mappedPercent = 99;
                    return true;
                case InstallProgressStages.Queue:
                case InstallProgressStages.Preparing:
                case InstallProgressStages.DownloadingLoaderInstaller:
                case InstallProgressStages.RunningLoaderInstaller:
                case InstallProgressStages.FinalizingVersion:
                case InstallProgressStages.CompletingFiles:
                case LaunchProgressStages.CheckingFiles:
                case LaunchProgressStages.DownloadingFiles:
                    installProgress = Math.Max(installProgress, normalizedPercent);
                    break;
                default:
                    if (value.Percent is null)
                    {
                        mappedPercent = 0;
                        return false;
                    }

                    mappedPercent = value.Percent.Value;
                    return true;
            }

            mappedPercent =
                (archiveProgress * ArchiveWeight) +
                (manifestProgress * ManifestWeight) +
                (instanceProgress * InstanceWeight) +
                (resolveProgress * ResolveWeight) +
                (installProgress * InstallWeight) +
                (downloadProgress * DownloadWeight) +
                (overridesProgress * OverridesWeight);
            return true;
        }

        private static bool IsMilestoneStage(string stage)
        {
            return stage is ImportProgressStages.PreparingArchive
                or ImportProgressStages.ParsingManifest
                or ImportProgressStages.CreatingInstance
                or ImportProgressStages.CopyingOverrides;
        }

        private static double NormalizePercent(double? percent, bool treatMissingAsComplete)
        {
            if (percent is null)
                return treatMissingAsComplete ? 1 : 0;

            return Math.Clamp(percent.Value, 0, 100) / 100d;
        }
    }
}
