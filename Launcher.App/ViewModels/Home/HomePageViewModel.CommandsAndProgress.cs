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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Home;

public sealed partial class HomePageViewModel
{
[RelayCommand(CanExecute = nameof(IsLaunching))]
    private void CancelLaunch()
    {
        var cancellationTokenSource = launchCancellationTokenSource;
        if (cancellationTokenSource is null || cancellationTokenSource.IsCancellationRequested)
            return;

        var instance = SelectedInstance;
        logger.LogInformation(
            "Launch cancellation requested. InstanceId={InstanceId} InstanceName={InstanceName} VersionName={VersionName}",
            instance?.Id,
            instance?.Name,
            instance?.VersionName);
        // 仅发出协作式取消，清理由 LaunchService 的阶段边界和 finally 负责。
        cancellationTokenSource.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedInstanceSettings))]
    private Task OpenSelectedInstanceSettingsAsync()
    {
        return openGameSettingsForInstance(SelectedInstance);
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            // Progress<T> 回到创建它的 UI 上下文；结束后的迟到进度必须丢弃，不能重新点亮进度条。
            if (!IsLaunching)
                return;

            if (progress.DownloadSpeedText is not null)
            {
                LaunchDownloadSpeedText = progress.DownloadSpeedText;
                return;
            }

            var message = FormatLaunchProgress(progress);
            LaunchStatusMessage = message;

            if (progress.Percent is double percent)
                LaunchProgressPercent = Math.Clamp(percent, 0, 100);

            statusService.Report(message);
            reportProgressPercent(LaunchProgressPercent);
        });
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        OnPropertyChanged(nameof(HasLaunchProgress));
        OnPropertyChanged(nameof(HasLaunchDownloadSpeedText));
        OnPropertyChanged(nameof(CanOpenSelectedInstanceSettings));
        LaunchCommand.NotifyCanExecuteChanged();
        CancelLaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedInstanceSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnLaunchStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchProgress));
    }

    partial void OnLaunchDownloadSpeedTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchDownloadSpeedText));
    }
}
