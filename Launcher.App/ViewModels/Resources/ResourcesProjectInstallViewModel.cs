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

using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesProjectInstallViewModel : ObservableObject
{
    private readonly ResourcesOnlineProjectPageOptions options;
    private readonly IResourceProjectInstallationService? installationService;
    private readonly ResourcesRequiredDependencyPlanner dependencyPlanner;
    private readonly IFilePickerService? filePickerService;
    private readonly IFloatingMessageService? floatingMessageService;
    private readonly DownloadTasksPageViewModel? downloadTasksPage;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private readonly Action<string> reportStatus;

    [ObservableProperty]
    private bool isInstalling;

    internal ResourcesProjectInstallViewModel(
        ResourcesOnlineProjectPageOptions options,
        IResourceProjectInstallationService? installationService,
        ResourcesRequiredDependencyPlanner dependencyPlanner,
        IFilePickerService? filePickerService,
        IFloatingMessageService? floatingMessageService,
        DownloadTasksPageViewModel? downloadTasksPage,
        IUiDispatcher uiDispatcher,
        ILogger? logger,
        Action<string> reportStatus)
    {
        this.options = options;
        this.installationService = installationService;
        this.dependencyPlanner = dependencyPlanner;
        this.filePickerService = filePickerService;
        this.floatingMessageService = floatingMessageService;
        this.downloadTasksPage = downloadTasksPage;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
        this.reportStatus = reportStatus;
    }

    public event Action<ResourcesModVersionItemViewModel>? FileExistsRequested;

    public event EventHandler<GameInstance>? ModpackImported;

    public event EventHandler<ResourcesModpackManualDownloadsRequestedEventArgs>? ModpackManualDownloadsRequested;

    internal async Task InstallAsync(
        ResourcesModVersionItemViewModel? item,
        ResourcesModInstallTargetItemViewModel? target,
        ResourcesModProjectItemViewModel? selectedProject,
        Func<IReadOnlyList<ResourcesModDependencyRequirementItemViewModel>, Task<RequiredDependenciesDialogChoice>> requestDependenciesDialogAsync)
    {
        if (item is null || target is null || installationService is null || IsInstalling)
            return;

        IsInstalling = true;
        DownloadTaskItem? downloadTask = null;
        try
        {
            if (target.IsLocalDownload)
            {
                var targetDirectory = filePickerService?.PickFolder(options.DownloadDirectoryPickerTitle);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                    return;
                var request = new ResourceProjectInstallationRequest(
                    item.Version,
                    ResourceProjectInstallationTargetKind.LocalDirectory,
                    TargetDirectory: targetDirectory);
                if ((await installationService.PrepareAsync(request).ConfigureAwait(false)).TargetExists)
                {
                    FileExistsRequested?.Invoke(item);
                    return;
                }

                downloadTask = BeginDownloadTask(item, targetDirectory);
                BeginUserFeedback(item);
                await installationService.ExecuteAsync(
                    request,
                    cancellationToken: downloadTask?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
                if (CompleteCanceledTask(downloadTask))
                    return;
                var message = string.Format(options.DownloadedFormat, ResolveFileName(item));
                CompleteTask(downloadTask, message);
                reportStatus(message);
                return;
            }

            if (target.IsNewInstanceInstall)
            {
                downloadTask = BeginDownloadTask(item, target.Title);
                BeginUserFeedback(item);
                var result = (await installationService.ExecuteAsync(
                    new ResourceProjectInstallationRequest(
                        item.Version,
                        ResourceProjectInstallationTargetKind.NewModpackInstance),
                    CreateImportProgress(downloadTask),
                    downloadTask?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false)).ModpackImportResult
                    ?? ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError);
                if (CompleteCanceledTask(downloadTask))
                    return;
                if (result.IsSuccess && result.ImportedInstance is not null)
                {
                    CompleteModpackImport(item, downloadTask, result, selectedProject);
                    return;
                }

                var failureMessage = MapModpackImportFailureMessage(result.FailureReason);
                floatingMessageService?.Show(failureMessage);
                reportStatus(failureMessage);
                FailTask(downloadTask, failureMessage);
                return;
            }

            var instance = target.Instance;
            if (instance is null)
                return;
            var instanceRequest = new ResourceProjectInstallationRequest(
                item.Version,
                ResourceProjectInstallationTargetKind.ExistingInstance,
                Instance: instance);
            if ((await installationService.PrepareAsync(instanceRequest).ConfigureAwait(false)).TargetExists)
            {
                FileExistsRequested?.Invoke(item);
                return;
            }

            var dependencyPlan = await dependencyPlanner.ResolveInstallPlanAsync(
                item,
                instance,
                selectedProject?.Project.ProjectId,
                requestDependenciesDialogAsync,
                CancellationToken.None).ConfigureAwait(false);
            if (dependencyPlan.Choice is RequiredDependenciesDialogChoice.Cancel)
                return;

            downloadTask = BeginDownloadTask(item, target.Title);
            floatingMessageService?.Show(options.DownloadingText);
            if (dependencyPlan.Choice is RequiredDependenciesDialogChoice.AutoInstallDependencies)
            {
                try
                {
                    await dependencyPlanner.InstallRequiredDependenciesAsync(
                        dependencyPlan.MissingDependencies,
                        instance,
                        selectedProject?.Project.ProjectId,
                        progress => ReportTask(downloadTask, progress),
                        downloadTask?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
                }
                catch (ResourceDependencyInstallException exception)
                {
                    var failureMessage = string.Format(
                        Strings.Status_ModRequiredDependenciesAutoInstallFailedFormat,
                        exception.DependencyTitle);
                    floatingMessageService?.Show(failureMessage);
                    reportStatus(failureMessage);
                    FailTask(downloadTask, failureMessage);
                    logger?.LogWarning(
                        exception,
                        "Failed to auto-install required resource dependency. ProjectId={ProjectId} DependencyProjectId={DependencyProjectId} InstanceId={InstanceId}",
                        selectedProject?.Project.ProjectId,
                        exception.DependencyProjectId,
                        instance.Id);
                    return;
                }
            }

            BeginUserFeedback(item);
            await installationService.ExecuteAsync(
                instanceRequest,
                cancellationToken: downloadTask?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
            if (CompleteCanceledTask(downloadTask))
                return;
            var installedMessage = string.Format(options.InstalledFormat, selectedProject?.Title ?? item.Title);
            CompleteTask(downloadTask, installedMessage);
            reportStatus(installedMessage);
        }
        catch (OperationCanceledException) when (downloadTask?.IsCancellationRequested == true)
        {
            CompleteCanceledTask(downloadTask);
            logger?.LogInformation(
                "Resource project installation canceled. ProjectId={ProjectId} VersionId={VersionId}",
                selectedProject?.Project.ProjectId,
                item.Version.VersionId);
        }
        catch (Exception exception)
        {
            var message = target.IsLocalDownload ? options.DownloadFailedText : options.InstallFailedText;
            reportStatus(message);
            floatingMessageService?.Show(message);
            FailTask(downloadTask, message);
            logger?.LogError(
                exception,
                "Resource project installation failed. Kind={Kind} ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId}",
                options.Kind,
                selectedProject?.Project.ProjectId,
                item.Version.VersionId,
                target.Instance?.Id);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void CompleteModpackImport(
        ResourcesModVersionItemViewModel item,
        DownloadTaskItem? downloadTask,
        ModpackImportResult result,
        ResourcesModProjectItemViewModel? selectedProject)
    {
        var instance = result.ImportedInstance!;
        var message = result.HasManualDownloads
            ? string.Format(Strings.Status_ModpackImportedWithManualDownloadsFormat, instance.Name)
            : string.Format(options.InstalledFormat, instance.Name);
        CompleteTask(downloadTask, message);
        reportStatus(message);
        uiDispatcher.Invoke(() =>
        {
            ModpackImported?.Invoke(this, instance);
            if (result.HasManualDownloads)
            {
                ModpackManualDownloadsRequested?.Invoke(
                    this,
                    new ResourcesModpackManualDownloadsRequestedEventArgs(instance, result.ManualDownloads));
            }
        });
        logger?.LogInformation(
            "Resource modpack imported as new instance. ProjectId={ProjectId} VersionId={VersionId} InstanceId={InstanceId} HasManualDownloads={HasManualDownloads}",
            selectedProject?.Project.ProjectId,
            item.Version.VersionId,
            instance.Id,
            result.HasManualDownloads);
    }

    private void BeginUserFeedback(ResourcesModVersionItemViewModel item)
    {
        floatingMessageService?.Show(options.DownloadingText);
        var message = string.Format(options.DownloadingFormat, item.Title);
        reportStatus(message);
    }

    private DownloadTaskItem? BeginDownloadTask(ResourcesModVersionItemViewModel item, string subtitle)
    {
        var task = downloadTasksPage?.BeginTask(item.Title, subtitle);
        ReportTask(task, new LauncherProgress(ModProgressStages.DownloadingFile, options.DownloadingText));
        return task;
    }

    private static void ReportTask(DownloadTaskItem? task, LauncherProgress progress)
    {
        task?.Report(progress with { Message = LauncherProgressTextFormatter.Format(progress) });
    }

    private static void CompleteTask(DownloadTaskItem? task, string message) => task?.Complete(message);

    private static void FailTask(DownloadTaskItem? task, string message) => task?.Fail(message);

    private bool CompleteCanceledTask(DownloadTaskItem? task)
    {
        return task?.IsCancellationRequested == true && CompleteCanceledTaskCore(task);
    }

    private bool CompleteCanceledTaskCore(DownloadTaskItem task)
    {
        downloadTasksPage?.CancelTask(task);
        return true;
    }

    private static IProgress<LauncherProgress>? CreateImportProgress(DownloadTaskItem? task)
    {
        return task is null ? null : new Progress<LauncherProgress>(progress => ReportTask(task, progress));
    }

    private string MapModpackImportFailureMessage(ModpackImportFailureReason failureReason)
    {
        return failureReason switch
        {
            ModpackImportFailureReason.FileNotFound
                or ModpackImportFailureReason.UnsupportedArchive
                or ModpackImportFailureReason.InvalidManifest => Strings.Status_ModpackInvalidArchive,
            ModpackImportFailureReason.UnsupportedLoader => Strings.Status_ModpackUnsupportedLoader,
            ModpackImportFailureReason.MissingCurseForgeApiKey => Strings.Status_ModpackMissingCurseForgeApiKey,
            ModpackImportFailureReason.HashMismatch => Strings.Status_ModpackHashMismatch,
            _ => options.InstallFailedText
        };
    }

    private static string ResolveFileName(ResourcesModVersionItemViewModel item)
    {
        return string.IsNullOrWhiteSpace(item.Version.FileName) ? item.Title : item.Version.FileName;
    }
}
