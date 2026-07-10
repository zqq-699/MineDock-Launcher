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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private TaskCompletionSource<RequiredDependenciesDialogChoice>? pendingDependenciesChoice;

    [ObservableProperty]
    private bool isInstalling;

    [ObservableProperty]
    private bool isFileExistsDialogOpen;

    [ObservableProperty]
    private string fileExistsDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isRequiredDependenciesDialogOpen;

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

    public event EventHandler<GameInstance>? ModpackImported;

    public event EventHandler<ResourcesModpackManualDownloadsRequestedEventArgs>? ModpackManualDownloadsRequested;

    public ObservableCollection<ResourcesModDependencyRequirementItemViewModel> RequiredDependencyDialogItems { get; } = [];

    [RelayCommand]
    private void CloseFileExistsDialog()
    {
        IsFileExistsDialogOpen = false;
        FileExistsDialogMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelRequiredDependenciesDialog()
    {
        ResolveDependenciesDialog(RequiredDependenciesDialogChoice.Cancel);
    }

    [RelayCommand]
    private void ContinueWithoutRequiredDependencies()
    {
        ResolveDependenciesDialog(RequiredDependenciesDialogChoice.ContinueWithoutDependencies);
    }

    [RelayCommand]
    private void AutoInstallRequiredDependencies()
    {
        ResolveDependenciesDialog(RequiredDependenciesDialogChoice.AutoInstallDependencies);
    }

    internal async Task InstallAsync(
        ResourcesModVersionItemViewModel? item,
        ResourcesModInstallTargetItemViewModel? target,
        ResourcesModProjectItemViewModel? selectedProject)
    {
        if (item is null || target is null || installationService is null || IsInstalling)
            return;

        IsInstalling = true;
        var context = new InstallOperationContext(item, target, selectedProject);
        try
        {
            if (target.IsLocalDownload)
                await DownloadToDirectoryAsync(context).ConfigureAwait(false);
            else if (target.IsNewInstanceInstall)
                await InstallModpackAsNewInstanceAsync(context).ConfigureAwait(false);
            else
                await InstallIntoExistingInstanceAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.Session?.IsCancellationRequested == true)
        {
            context.Session.CompleteCancellation();
            logger?.LogInformation(
                "Resource project installation canceled. ProjectId={ProjectId} VersionId={VersionId}",
                selectedProject?.Project.ProjectId,
                item.Version.VersionId);
        }
        catch (Exception exception)
        {
            var message = target.IsLocalDownload ? options.DownloadFailedText : options.InstallFailedText;
            PresentFailure(context, message);
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

    private async Task DownloadToDirectoryAsync(InstallOperationContext context)
    {
        var targetDirectory = filePickerService?.PickFolder(options.DownloadDirectoryPickerTitle);
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return;
        var request = new ResourceProjectInstallationRequest(
            context.Item.Version,
            ResourceProjectInstallationTargetKind.LocalDirectory,
            TargetDirectory: targetDirectory);
        if ((await installationService!.PrepareAsync(request).ConfigureAwait(false)).TargetExists)
        {
            ShowFileExists(context.Item);
            return;
        }
        BeginSession(context, targetDirectory);
        BeginUserFeedback(context.Item);
        await installationService.ExecuteAsync(request, cancellationToken: context.Session!.CancellationToken)
            .ConfigureAwait(false);
        if (context.Session.CompleteCancellation())
            return;
        var message = string.Format(options.DownloadedFormat, ResolveFileName(context.Item));
        context.Session.Complete(message);
        reportStatus(message);
    }

    private async Task InstallModpackAsNewInstanceAsync(InstallOperationContext context)
    {
        BeginSession(context, context.Target.Title);
        BeginUserFeedback(context.Item);
        var result = (await installationService!.ExecuteAsync(
            new ResourceProjectInstallationRequest(
                context.Item.Version,
                ResourceProjectInstallationTargetKind.NewModpackInstance),
            context.Session!.Progress,
            context.Session.CancellationToken).ConfigureAwait(false)).ModpackImportResult
            ?? ModpackImportResult.Failure(ModpackImportFailureReason.UnexpectedError);
        if (context.Session.CompleteCancellation())
            return;
        if (result.IsSuccess && result.ImportedInstance is not null)
        {
            CompleteModpackImport(context, result);
            return;
        }
        PresentFailure(context, MapModpackImportFailureMessage(result.FailureReason));
    }

    private async Task InstallIntoExistingInstanceAsync(InstallOperationContext context)
    {
        var instance = context.Target.Instance;
        if (instance is null)
            return;
        var request = new ResourceProjectInstallationRequest(
            context.Item.Version,
            ResourceProjectInstallationTargetKind.ExistingInstance,
            Instance: instance);
        if ((await installationService!.PrepareAsync(request).ConfigureAwait(false)).TargetExists)
        {
            ShowFileExists(context.Item);
            return;
        }
        var dependencyPlan = await dependencyPlanner.ResolveInstallPlanAsync(
            context.Item,
            instance,
            context.Project?.Project.ProjectId,
            RequestDependenciesDialogAsync,
            CancellationToken.None).ConfigureAwait(false);
        if (dependencyPlan.Choice is RequiredDependenciesDialogChoice.Cancel)
            return;

        BeginSession(context, context.Target.Title);
        floatingMessageService?.Show(options.DownloadingText);
        if (dependencyPlan.Choice is RequiredDependenciesDialogChoice.AutoInstallDependencies
            && !await InstallDependenciesAsync(context, dependencyPlan, instance).ConfigureAwait(false))
        {
            return;
        }

        BeginUserFeedback(context.Item);
        await installationService.ExecuteAsync(request, cancellationToken: context.Session!.CancellationToken)
            .ConfigureAwait(false);
        if (context.Session.CompleteCancellation())
            return;
        var message = string.Format(options.InstalledFormat, context.Project?.Title ?? context.Item.Title);
        context.Session.Complete(message);
        reportStatus(message);
    }

    private async Task<bool> InstallDependenciesAsync(
        InstallOperationContext context,
        RequiredDependencyInstallPlan dependencyPlan,
        GameInstance instance)
    {
        try
        {
            await dependencyPlanner.InstallRequiredDependenciesAsync(
                dependencyPlan.MissingDependencies,
                instance,
                context.Project?.Project.ProjectId,
                progress => context.Session!.Report(progress),
                context.Session!.CancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ResourceDependencyInstallException exception)
        {
            var message = string.Format(
                Strings.Status_ModRequiredDependenciesAutoInstallFailedFormat,
                exception.DependencyTitle);
            PresentFailure(context, message);
            logger?.LogWarning(
                exception,
                "Failed to auto-install required resource dependency. ProjectId={ProjectId} DependencyProjectId={DependencyProjectId} InstanceId={InstanceId}",
                context.Project?.Project.ProjectId,
                exception.DependencyProjectId,
                instance.Id);
            return false;
        }
    }

    private void ShowFileExists(ResourcesModVersionItemViewModel item)
    {
        uiDispatcher.Invoke(() =>
        {
            var fileName = string.IsNullOrWhiteSpace(item.Version.FileName) ? item.Title : item.Version.FileName;
            FileExistsDialogMessage = string.Format(options.FileExistsMessageFormat, fileName);
            IsFileExistsDialogOpen = true;
        });
    }

    private async Task<RequiredDependenciesDialogChoice> RequestDependenciesDialogAsync(
        IReadOnlyList<ResourcesModDependencyRequirementItemViewModel> items)
    {
        pendingDependenciesChoice?.TrySetResult(RequiredDependenciesDialogChoice.Cancel);
        var completion = new TaskCompletionSource<RequiredDependenciesDialogChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pendingDependenciesChoice = completion;
        uiDispatcher.Invoke(() =>
        {
            RequiredDependencyDialogItems.Clear();
            foreach (var item in items)
                RequiredDependencyDialogItems.Add(item);
            IsRequiredDependenciesDialogOpen = true;
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private void ResolveDependenciesDialog(RequiredDependenciesDialogChoice choice)
    {
        uiDispatcher.Invoke(() => IsRequiredDependenciesDialogOpen = false);
        pendingDependenciesChoice?.TrySetResult(choice);
        pendingDependenciesChoice = null;
    }

    private void CompleteModpackImport(InstallOperationContext context, ModpackImportResult result)
    {
        var instance = result.ImportedInstance!;
        var message = result.HasManualDownloads
            ? string.Format(Strings.Status_ModpackImportedWithManualDownloadsFormat, instance.Name)
            : string.Format(options.InstalledFormat, instance.Name);
        context.Session?.Complete(message);
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
            context.Project?.Project.ProjectId,
            context.Item.Version.VersionId,
            instance.Id,
            result.HasManualDownloads);
    }

    private void BeginUserFeedback(ResourcesModVersionItemViewModel item)
    {
        floatingMessageService?.Show(options.DownloadingText);
        var message = string.Format(options.DownloadingFormat, item.Title);
        reportStatus(message);
    }

    private void BeginSession(InstallOperationContext context, string subtitle)
    {
        context.Session = ResourceInstallTaskSession.Begin(
            downloadTasksPage,
            context.Item.Title,
            subtitle,
            options.DownloadingText);
    }

    private void PresentFailure(InstallOperationContext context, string message)
    {
        floatingMessageService?.Show(message);
        reportStatus(message);
        context.Session?.Fail(message);
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

    private sealed class InstallOperationContext(
        ResourcesModVersionItemViewModel item,
        ResourcesModInstallTargetItemViewModel target,
        ResourcesModProjectItemViewModel? project)
    {
        public ResourcesModVersionItemViewModel Item { get; } = item;
        public ResourcesModInstallTargetItemViewModel Target { get; } = target;
        public ResourcesModProjectItemViewModel? Project { get; } = project;
        public ResourceInstallTaskSession? Session { get; set; }
    }
}
