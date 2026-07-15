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
private async Task<bool> InstallDependenciesAsync(
        InstallOperationContext context,
        RequiredDependencyInstallPlan dependencyPlan,
        GameInstance instance)
    {
        // 按规划顺序逐个安装，让失败时的已完成集合和进度顺序保持可解释。
        try
        {
            context.Session!.BeginDependencies(dependencyPlan.MissingDependencies.Count);
            await dependencyPlanner.InstallRequiredDependenciesAsync(
                dependencyPlan.MissingDependencies,
                instance,
                context.Project?.Project.ProjectId,
                context.Session.Progress,
                progress => context.Session!.ReportDependencyStarted(progress),
                context.Session!.CancellationToken).ConfigureAwait(false);
            context.Session.CompleteDependencies();
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
}
