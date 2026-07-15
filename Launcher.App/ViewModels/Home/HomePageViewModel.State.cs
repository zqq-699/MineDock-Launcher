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
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Home;

public sealed partial class HomePageViewModel
{
private void LaunchGames_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 对外属性多为子 ViewModel 投影，需要显式转发通知，否则首页 Binding 不会感知子对象变化。
        switch (e.PropertyName)
        {
            case nameof(HomeLaunchGameListViewModel.SelectedInstance):
                OnPropertyChanged(nameof(SelectedInstance));
                NotifyInstanceStateChanged();
                break;
            case nameof(HomeLaunchGameListViewModel.HasLaunchInstances):
                OnPropertyChanged(nameof(HasLaunchInstances));
                break;
            case nameof(HomeLaunchGameListViewModel.HasNoLaunchInstances):
                OnPropertyChanged(nameof(HasNoLaunchInstances));
                break;
            case nameof(HomeLaunchGameListViewModel.SelectedLaunchInstanceItem):
                OnPropertyChanged(nameof(SelectedLaunchInstanceItem));
                break;
            case nameof(HomeLaunchGameListViewModel.HasSelectedLaunchInstance):
                OnPropertyChanged(nameof(HasSelectedLaunchInstance));
                break;
            case nameof(HomeLaunchGameListViewModel.IsLaunchMenuPinned):
                OnPropertyChanged(nameof(IsLaunchMenuPinned));
                break;
        }
    }

    private void NotifyAccountStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(HomeAvatarUrl));
        OnPropertyChanged(nameof(HomeAccountDisplayName));
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        LaunchCommand.NotifyCanExecuteChanged();
    }

    private void NotifyInstanceStateChanged()
    {
        OnPropertyChanged(nameof(HomeVersionDisplayName));
        OnPropertyChanged(nameof(CanLaunchSelectedGame));
        OnPropertyChanged(nameof(CanOpenSelectedInstanceSettings));
        LaunchCommand.NotifyCanExecuteChanged();
        OpenSelectedInstanceSettingsCommand.NotifyCanExecuteChanged();
    }

    private CancellationTokenSource BeginLaunchProgress()
    {
        // 上一次会话理论上已结束，仍先释放旧 CTS，避免异常路径遗留句柄。
        launchCancellationTokenSource?.Dispose();
        launchSpeedMeterLifetime?.Dispose();
        launchCancellationTokenSource = new CancellationTokenSource();
        IProgress<LauncherProgress> uiProgress = new Progress<LauncherProgress>(ReportLaunchProgress);
        launchProgress = DownloadSpeedTaskProgress.Create(
            report: uiProgress.Report,
            reportTelemetry: uiProgress.Report,
            out var speedMeterLifetime);
        launchSpeedMeterLifetime = speedMeterLifetime;
        IsLaunching = true;
        LaunchProgressPercent = 0;
        LaunchDownloadSpeedText = string.Empty;
        LaunchStatusMessage = Strings.Status_LaunchPreparing;
        statusService.Report(LaunchStatusMessage);
        reportProgressPercent(LaunchProgressPercent);
        return launchCancellationTokenSource;
    }

    private void ResetLaunchProgress()
    {
        launchSpeedMeterLifetime?.Dispose();
        launchSpeedMeterLifetime = null;
        launchProgress = null;
        launchCancellationTokenSource?.Dispose();
        launchCancellationTokenSource = null;
        LaunchProgressPercent = 0;
        reportProgressPercent(0);
        LaunchDownloadSpeedText = string.Empty;
        LaunchStatusMessage = string.Empty;
        IsLaunching = false;
    }

    private bool ShouldMinimizeLauncherAfterLaunch(GameInstance instance)
    {
        return instance.LaunchSettingsMode is LaunchSettingsMode.UseGlobal
            ? settings.DefaultMinimizeLauncherAfterLaunch
            : instance.MinimizeLauncherAfterLaunch;
    }

    private static string FormatLaunchProgress(LauncherProgress progress)
    {
        // 已知阶段使用本地化稳定文案；仅对扩展阶段回退到服务提供的消息。
        return progress.Stage switch
        {
            LaunchProgressStages.CheckingInstance => Strings.Status_LaunchCheckingInstance,
            LaunchProgressStages.RepairingMetadata => Strings.Status_LaunchRepairingMetadata,
            LaunchProgressStages.RepairingLoaderInstaller => Strings.Status_LaunchRepairingLoaderInstaller,
            LaunchProgressStages.RunningLoaderInstaller => Strings.Status_LaunchRunningLoaderInstaller,
            LaunchProgressStages.RepairingJar => Strings.Status_LaunchRepairingJar,
            LaunchProgressStages.RepairingLibraries => Strings.Status_LaunchRepairingLibraries,
            LaunchProgressStages.RepairingAssets => Strings.Status_LaunchRepairingAssets,
            LaunchProgressStages.RepairingLogging => Strings.Status_LaunchRepairingLogging,
            LaunchProgressStages.CheckingJava => Strings.Status_LaunchCheckingJava,
            LaunchProgressStages.RunningPreLaunchCommand => Strings.Status_LaunchRunningPreLaunchCommand,
            LaunchProgressStages.PreparingProcess => Strings.Status_LaunchPreparingProcess,
            LaunchProgressStages.StartingProcess => Strings.Status_LaunchStartingProcess,
            LaunchProgressStages.CheckingFiles => Strings.Status_LaunchCheckingFiles,
            LaunchProgressStages.DownloadingFiles => Strings.Status_LaunchDownloadingFiles,
            _ when !string.IsNullOrWhiteSpace(progress.Message) => progress.Message,
            _ => Strings.Status_LaunchPreparing
        };
    }
}
