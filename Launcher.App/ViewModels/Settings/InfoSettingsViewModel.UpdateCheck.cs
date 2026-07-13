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

using System.Reflection;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class InfoSettingsViewModel
{
private async Task CheckUpdatesCoreAsync(UpdateCheckPresentation presentation)
    {
        // 所有入口共享同一 Busy 门闩，防止手动检查与启动检查同时覆盖结果状态。
        if (isUpdateCheckRunning)
            return;

        var channel = SelectedUpdateChannelOption?.Channel ?? LauncherDefaults.DefaultUpdateChannel;
        isUpdateCheckRunning = true;
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        if (presentation is UpdateCheckPresentation.Manual)
        {
            IsCheckingUpdates = true;
            statusService.Report(Strings.Status_CheckingUpdates);
        }
        else
        {
            logger.LogInformation(
                "Startup launcher update check started. CurrentVersion={CurrentVersion} Channel={Channel}",
                LauncherVersionText,
                channel);
        }

        try
        {
            // 远端结果必须比当前语义版本新才显示；无法解析版本时按服务提供的可用性处理。
            LauncherUpdateCheckResult result;
            try
            {
                result = await launcherUpdateService.CheckForUpdatesAsync(LauncherVersionText, channel);
            }
            catch (Exception exception)
            {
                if (presentation is UpdateCheckPresentation.Manual)
                {
                    ReportVisibleStatus(Strings.Status_CheckUpdatesFailed);
                }
                else
                {
                    logger.LogWarning(
                        exception,
                        "Startup launcher update check threw an exception. CurrentVersion={CurrentVersion} Channel={Channel}",
                        LauncherVersionText,
                        channel);
                }

                return;
            }

            if (result.IsFailed)
            {
                if (presentation is UpdateCheckPresentation.Manual)
                {
                    ReportVisibleStatus(Strings.Status_CheckUpdatesFailed);
                }
                else
                {
                    logger.LogWarning(
                        "Startup launcher update check failed. CurrentVersion={CurrentVersion} Channel={Channel} Error={Error}",
                        LauncherVersionText,
                        channel,
                        string.IsNullOrWhiteSpace(result.ErrorMessage) ? "<none>" : result.ErrorMessage);
                }

                return;
            }

            if (!result.IsUpdateAvailable || result.Update is null)
            {
                if (presentation is UpdateCheckPresentation.Manual)
                {
                    ReportVisibleStatus(Strings.Status_LauncherAlreadyLatest);
                }
                else
                {
                    logger.LogInformation(
                        "Startup launcher update check completed. No update available. CurrentVersion={CurrentVersion} Channel={Channel}",
                        LauncherVersionText,
                        channel);
                }

                return;
            }

            if (presentation is UpdateCheckPresentation.StartupSilent)
            {
                logger.LogInformation(
                    "Startup launcher update check found an update. CurrentVersion={CurrentVersion} Channel={Channel} UpdateVersion={UpdateVersion}",
                    LauncherVersionText,
                    channel,
                    result.Update.DisplayVersion);
            }

            ShowUpdateAvailableDialog(result.Update);
        }
        finally
        {
            if (presentation is UpdateCheckPresentation.Manual)
                IsCheckingUpdates = false;

            isUpdateCheckRunning = false;
            CheckUpdatesCommand.NotifyCanExecuteChanged();
        }
    }
}
