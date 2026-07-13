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

using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadPageViewModel
{
private void VersionList_VersionSelected(DownloadMinecraftVersionItem version)
    {
        _ = OpenInstanceOptionsAsync(version);
    }

    private async Task OpenInstanceOptionsAsync(DownloadMinecraftVersionItem version)
    {
        // 版本切换会触发 Loader 查询，先取消上一次导航，防止旧结果晚到覆盖当前页。
        CancelOptionsNavigation();
        var cancellation = new CancellationTokenSource();
        optionsNavigationCancellation = cancellation;
        try
        {
            var preparation = InstanceOptions.PrepareAsync(version, cancellation.Token);
            if (!ReferenceEquals(optionsNavigationCancellation, cancellation)
                || !ReferenceEquals(VersionList.SelectedMinecraftVersion, version))
            {
                await preparation;
                return;
            }

            CurrentStep = DownloadPageStep.InstanceOptions;
            await preparation;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare instance installation options. MinecraftVersion={MinecraftVersion}",
                version.Name);
            if (ReferenceEquals(optionsNavigationCancellation, cancellation))
                CurrentStep = DownloadPageStep.InstanceOptions;
        }
        finally
        {
            Interlocked.CompareExchange(ref optionsNavigationCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void VersionList_LocalImportRequested()
    {
        LocalImportDialog.Open();
    }

    private void VersionList_CategoryContentRefreshRequested()
    {
        ContentRefreshToken++;
    }

    private void VersionList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 页面公开属性是子列表状态投影，需要显式转发通知保持 Binding 更新。
        switch (e.PropertyName)
        {
            case nameof(DownloadVersionListViewModel.SelectedVersionCategory):
                if (IsInstanceOptionsStep)
                    ShowVersionList();
                OnPropertyChanged(nameof(PageTitle));
                break;
            case nameof(DownloadVersionListViewModel.SelectedMinecraftVersion):
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(PageTitleIconSource));
                break;
            case nameof(DownloadVersionListViewModel.VisibleVersions):
            case nameof(DownloadVersionListViewModel.HasVisibleVersions):
                OnPropertyChanged(nameof(IsDownloadContentVisible));
                break;
        }
    }

    private void InstanceOptions_InstallAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanInstallSelectedVersion));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private void InstallState_NameAvailabilityChanged()
    {
        InstanceOptions.NotifyNameAvailabilityChanged();
    }

    private void InstallState_InstanceInstalled(object? sender, GameInstance instance)
    {
        InstanceInstalled?.Invoke(this, instance);
    }

    private void LocalImportDialog_ModpackImported(object? sender, GameInstance instance)
    {
        InstanceInstalled?.Invoke(this, instance);
    }
}
