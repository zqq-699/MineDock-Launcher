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

public sealed partial class ResourcesProjectInstallViewModel
{
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
        context.Session!.BeginPrimaryDownload(hasDependencies: false);
        await installationService.ExecuteAsync(request, context.Session.Progress, context.Session.CancellationToken)
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
        context.Session!.BeginModpackImport();
        var result = (await installationService!.ExecuteAsync(
            new ResourceProjectInstallationRequest(
                context.Item.Version,
                ResourceProjectInstallationTargetKind.NewModpackInstance,
                Project: context.Project?.Project),
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
        context.Session!.BeginPrimaryDownload(
            dependencyPlan.Choice is RequiredDependenciesDialogChoice.AutoInstallDependencies
                && dependencyPlan.MissingDependencies.Count > 0);
        await installationService.ExecuteAsync(request, context.Session.Progress, context.Session.CancellationToken)
            .ConfigureAwait(false);
        if (context.Session.CompleteCancellation())
            return;
        var message = string.Format(options.InstalledFormat, context.Project?.Title ?? context.Item.Title);
        context.Session.Complete(message);
        reportStatus(message);
    }
}
