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
[RelayCommand]
    private void BackToVersionList()
    {
        ShowVersionList();
    }

    [RelayCommand(CanExecute = nameof(CanInstallSelectedVersion), AllowConcurrentExecutions = true)]
    private async Task InstallAsync()
    {
        // 安装按钮委托给 InstallState，本页只负责防重复提交和步骤切换。
        var request = InstanceOptions.CreateInstallRequest();
        if (request is null)
            return;

        CurrentStep = DownloadPageStep.VersionList;
        ContentRefreshToken++;
        var operation = InstallState.InstallAsync(request);
        downloadTasksPage.TrackBackgroundTask(operation);
        await operation;
    }

    partial void OnCurrentStepChanged(DownloadPageStep value)
    {
        OnPropertyChanged(nameof(IsVersionListStep));
        OnPropertyChanged(nameof(IsInstanceOptionsStep));
        OnPropertyChanged(nameof(IsDownloadContentVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageTitleIconSource));
        OnPropertyChanged(nameof(CanInstallSelectedVersion));
        InstallCommand.NotifyCanExecuteChanged();
    }
}
