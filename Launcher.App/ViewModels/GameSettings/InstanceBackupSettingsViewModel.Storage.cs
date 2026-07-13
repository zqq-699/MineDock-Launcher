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
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceBackupSettingsViewModel
{
[RelayCommand]
    private async Task ChangeBackupDirectoryAsync()
    {
        if (selectedInstance is null)
            return;

        // 文件选择器只负责取得候选路径，规范化和设置持久化在确认后完成。
        var selectedDirectory = filePickerService.PickFolder(
            Strings.FilePicker_BackupDirectoryTitle,
            string.IsNullOrWhiteSpace(BackupDirectory) ? null : BackupDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        // GetFullPath 提前拒绝无效输入，避免保存一个后续每次访问都会失败的路径。
        string normalizedDirectory;
        try
        {
            normalizedDirectory = await backupService.EnsureBackupDirectoryAsync(selectedDirectory);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to change instance backup directory. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_BackupDirectoryChangeFailed);
            return;
        }

        if (string.Equals(BackupDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        var originalDirectory = selectedInstance.BackupDirectory;
        try
        {
            // 先持久化实例设置，再切换页面目录；保存失败时继续显示原目录。
            selectedInstance.BackupDirectory = normalizedDirectory;
            await instanceService.SaveInstanceAsync(selectedInstance);
            BackupDirectory = normalizedDirectory;
            Parent.NotifyInstanceSettingsSaved(selectedInstance);
        }
        catch (Exception exception)
        {
            selectedInstance.BackupDirectory = originalDirectory;
            BackupDirectory = originalDirectory;
            logger.LogError(
                exception,
                "Failed to save instance backup directory. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_BackupDirectoryChangeFailed);
            return;
        }

        statusService.Report(Strings.Status_BackupDirectoryChanged);
        await RefreshBackupsAsync();
    }
}
