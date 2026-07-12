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

/// <summary>
/// 编排资源版本安装的用户决策、依赖处理和结果反馈，并按资源类型选择对应安装路径。
/// </summary>
public sealed partial class ResourcesProjectInstallViewModel : ObservableObject
{
    // 对话框用 TaskCompletionSource 把按钮事件转换为可等待决策，使主安装流程仍能顺序表达。
    private readonly ResourcesOnlineProjectPageOptions options;
    private readonly IResourceProjectInstallationService? installationService;
    private readonly ResourcesRequiredDependencyPlanner dependencyPlanner;
    private readonly IFilePickerService? filePickerService;
    private readonly IFloatingMessageService? floatingMessageService;
    private readonly DownloadTasksPageViewModel? downloadTasksPage;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private readonly Action<string> reportStatus;
    private readonly object installStateLock = new();
    private int activeInstallCount;
    private bool hasExclusiveInstall;
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
        // 每次安装冻结目标、版本和文件名，防止用户切换页面选择后影响在途操作。
        if (item is null || target is null || installationService is null)
            return;

        if (!TryBeginInstall(target.IsNewInstanceInstall))
            return;
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
        catch (ResourceProjectIntegrityException exception)
        {
            PresentFailure(context, Strings.Status_ResourceProjectIntegrityFailed);
            logger?.LogWarning(
                exception,
                "Resource project integrity verification prevented installation. Kind={Kind} ProjectId={ProjectId} VersionId={VersionId} Reason={Reason} Algorithm={Algorithm}",
                options.Kind,
                selectedProject?.Project.ProjectId,
                item.Version.VersionId,
                exception.Reason,
                exception.Algorithm);
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
            EndInstall();
        }
    }

    private bool TryBeginInstall(bool supportsParallelInstall)
    {
        // 新实例整合包拥有独立任务会话和临时工作区，只与同类任务并行。
        // 其他资源路径继续独占安装器，避免共享依赖确认对话框出现并发竞争。
        lock (installStateLock)
        {
            if (supportsParallelInstall ? hasExclusiveInstall : activeInstallCount > 0)
                return false;

            activeInstallCount++;
            hasExclusiveInstall = !supportsParallelInstall;
            IsInstalling = true;
            return true;
        }
    }

    private void EndInstall()
    {
        lock (installStateLock)
        {
            activeInstallCount = Math.Max(0, activeInstallCount - 1);
            if (activeInstallCount == 0)
                hasExclusiveInstall = false;
            IsInstalling = activeInstallCount > 0;
        }
    }

    private async Task DownloadToDirectoryAsync(InstallOperationContext context)
    {
        // 同名文件不直接覆盖，先交还给用户确认，避免普通下载造成不可逆的数据替换。
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
        // 整合包导入拥有独立的安装、清理和手动下载结果，本层只映射页面事件与反馈。
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
        // 在主文件写入前规划 Required 依赖，用户取消时不会留下主资源已安装但依赖缺失的状态。
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
        // 按规划顺序逐个安装，让失败时的已完成集合和进度顺序保持可解释。
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
        // 异步 continuation 避免按钮事件完成 Task 时同步重入后续安装并阻塞 UI 调用栈。
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
        // 会话开始统一清空上一轮错误、进度和弹窗状态，避免迟到反馈与新安装混合。
        context.Session = ResourceInstallTaskSession.Begin(
            downloadTasksPage,
            context.Item.Title,
            subtitle,
            options.DownloadingText);
    }

    private void PresentFailure(InstallOperationContext context, string message)
    {
        // 页面只展示稳定、本地化的失败原因，底层异常细节由业务服务写入日志。
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
