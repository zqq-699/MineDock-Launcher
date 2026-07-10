/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

public sealed class LocalModpackImportService : ILocalModpackImportService
{
    private readonly IModpackPackageService modpackPackageService;
    private readonly IModpackGameInstaller modpackGameInstaller;
    private readonly IModpackInstanceStagingService stagingService;
    private readonly IGameInstallCoordinator installCoordinator;
    private readonly ModpackImportCleanupCoordinator cleanupCoordinator;
    private readonly ILogger<LocalModpackImportService> logger;

    public LocalModpackImportService(
        IGameInstanceService instanceService,
        IModpackPackageService modpackPackageService,
        IModpackGameInstaller modpackGameInstaller,
        IModpackInstanceStagingService stagingService,
        IGameInstallCoordinator? installCoordinator = null,
        ILogger<LocalModpackImportService>? logger = null)
    {
        this.modpackPackageService = modpackPackageService;
        this.modpackGameInstaller = modpackGameInstaller;
        this.stagingService = stagingService;
        this.installCoordinator = installCoordinator ?? new GameInstallCoordinator();
        this.logger = logger ?? NullLogger<LocalModpackImportService>.Instance;
        cleanupCoordinator = new ModpackImportCleanupCoordinator(
            instanceService,
            modpackPackageService,
            stagingService,
            this.logger);
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
        var importProgress = progress is null ? null : new OverallModpackImportProgress(progress);
        var session = new ModpackImportSession(importProgress);

        try
        {
            await PrepareAndStageAsync(session, archivePath, cancellationToken).ConfigureAwait(false);
            var manualDownloads = await InstallGameAndContentAsync(
                    session,
                    downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond,
                    cancellationToken)
                .ConfigureAwait(false);
            await FinalizeImportAsync(session, manualDownloads, cancellationToken).ConfigureAwait(false);

            var preparedModpack = session.PreparedModpack!;
            var importedInstance = session.ImportedInstance!;
            await cleanupCoordinator.CleanupSuccessfulImportAsync(preparedModpack).ConfigureAwait(false);

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
            await cleanupCoordinator.CleanupFailedImportAsync(session).ConfigureAwait(false);
            return ModpackImportResult.Failure(exception.FailureReason);
        }
        catch (OperationCanceledException)
        {
            await cleanupCoordinator.CleanupFailedImportAsync(session).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected modpack import failure. ArchivePath={ArchivePath}",
                archivePath);
            await cleanupCoordinator.CleanupFailedImportAsync(session).ConfigureAwait(false);
            return ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError);
        }
    }

    private async Task PrepareAndStageAsync(
        ModpackImportSession session,
        string archivePath,
        CancellationToken cancellationToken)
    {
        session.Progress?.Report(new LauncherProgress(ImportProgressStages.PreparingArchive, string.Empty));
        session.Progress?.Report(new LauncherProgress(ImportProgressStages.ParsingManifest, string.Empty));
        session.PreparedModpack = await modpackPackageService
            .PrepareAsync(archivePath, cancellationToken, session.Progress)
            .ConfigureAwait(false);
        var preferredInstanceName = NormalizePreferredInstanceName(session.PreparedModpack.PackageName);

        logger.LogInformation(
            "Importing modpack. ArchivePath={ArchivePath} ModpackName={ModpackName} MinecraftVersion={MinecraftVersion} Loader={Loader} LoaderVersion={LoaderVersion} PreferredInstanceName={PreferredInstanceName}",
            archivePath,
            session.PreparedModpack.PackageName,
            session.PreparedModpack.MinecraftVersion,
            session.PreparedModpack.Loader,
            session.PreparedModpack.LoaderVersion,
            preferredInstanceName);

        session.Progress?.Report(new LauncherProgress(ImportProgressStages.CreatingInstance, string.Empty));
        session.StagedInstance = await stagingService
            .StageAsync(session.PreparedModpack, preferredInstanceName, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Modpack instance staged. ArchivePath={ArchivePath} PreferredInstanceName={PreferredInstanceName} ResolvedInstanceName={ResolvedInstanceName} InstanceDirectory={InstanceDirectory}",
            archivePath,
            preferredInstanceName,
            session.StagedInstance.ResolvedInstanceName,
            session.StagedInstance.InstanceDirectory);
        session.Progress?.Report(new LauncherProgress(ImportProgressStages.CreatingInstance, string.Empty, 100));
    }

    private async Task<IReadOnlyList<ManualModpackDownload>> InstallGameAndContentAsync(
        ModpackImportSession session,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        var preparedModpack = session.PreparedModpack!;
        var stagedInstance = session.StagedInstance!;
        using var importCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var importCancellationToken = importCancellation.Token;

        var downloadModsTask = CompleteWithProgressAsync(
            modpackPackageService.DownloadFilesAsync(
                preparedModpack,
                stagedInstance.Instance,
                session.Progress,
                importCancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond),
            session.Progress,
            new LauncherProgress(ImportProgressStages.DownloadingPackFiles, string.Empty, 100));
        _ = CancelImportOnBranchFailureAsync(downloadModsTask, importCancellation);

        var installLeaseTask = installCoordinator
            .AcquireInstallAsync(
                stagedInstance.MinecraftDirectory,
                stagedInstance.ResolvedInstanceName,
                session.Progress,
                importCancellationToken)
            .AsTask();
        await ThrowContentFailureBeforeInstallLeaseAsync(downloadModsTask, installLeaseTask, importCancellation)
            .ConfigureAwait(false);

        await using var installLease = await installLeaseTask.ConfigureAwait(false);
        var minecraftBaseTask = modpackGameInstaller.InstallMinecraftBaseAsync(
            preparedModpack.MinecraftVersion,
            stagedInstance.MinecraftDirectory,
            CreateInstallStageProgress(session.Progress, ImportProgressStages.InstallingMinecraftBase),
            importCancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond);
        var loaderInstallTask = InstallLoaderAfterBaseAsync(
            preparedModpack,
            stagedInstance,
            minecraftBaseTask,
            session.Progress,
            importCancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond);
        _ = CancelImportOnBranchFailureAsync(loaderInstallTask, importCancellation);

        await AwaitImportBranchesAsync(importCancellation, loaderInstallTask, downloadModsTask)
            .ConfigureAwait(false);
        session.FinalVersionName = await loaderInstallTask.ConfigureAwait(false);
        return await downloadModsTask.ConfigureAwait(false);
    }

    private async Task FinalizeImportAsync(
        ModpackImportSession session,
        IReadOnlyList<ManualModpackDownload> manualDownloads,
        CancellationToken cancellationToken)
    {
        var preparedModpack = session.PreparedModpack!;
        var stagedInstance = session.StagedInstance!;
        await CompleteWithProgressAsync(
                modpackPackageService.CopyOverridesAsync(
                    preparedModpack,
                    stagedInstance.Instance,
                    session.Progress,
                    cancellationToken),
                session.Progress,
                new LauncherProgress(ImportProgressStages.CopyingOverrides, string.Empty, 100))
            .ConfigureAwait(false);
        session.ImportedInstance = await stagingService
            .FinalizeAsync(stagedInstance, session.FinalVersionName!, cancellationToken)
            .ConfigureAwait(false);

        preparedModpack.ManualDownloads = manualDownloads;
        preparedModpack.ManualDownloadsFilePath = await modpackPackageService
            .WriteManualDownloadsFileAsync(
                preparedModpack,
                session.ImportedInstance,
                manualDownloads,
                cancellationToken)
            .ConfigureAwait(false);
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

    private static string NormalizePreferredInstanceName(string preferredName)
    {
        var normalizedName = VersionDirectoryName.Sanitize(preferredName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "Prepared modpack package name is missing.");
        }

        return normalizedName;
    }

    private static async Task ThrowContentFailureBeforeInstallLeaseAsync(
        Task contentTask,
        Task<IAsyncDisposable> installLeaseTask,
        CancellationTokenSource importCancellation)
    {
        var firstCompleted = await Task.WhenAny(contentTask, installLeaseTask).ConfigureAwait(false);
        if (!ReferenceEquals(firstCompleted, contentTask) || contentTask.IsCompletedSuccessfully)
            return;

        SafeCancel(importCancellation);
        try
        {
            await using var abandonedLease = await installLeaseTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await contentTask.ConfigureAwait(false);
    }

    private static async Task AwaitImportBranchesAsync(
        CancellationTokenSource importCancellation,
        params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            SafeCancel(importCancellation);
            throw;
        }
    }

    private static async Task CancelImportOnBranchFailureAsync(
        Task branchTask,
        CancellationTokenSource importCancellation)
    {
        try
        {
            await branchTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            SafeCancel(importCancellation);
        }
    }

    private static void SafeCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

}
