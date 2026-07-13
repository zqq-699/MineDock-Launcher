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

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Domain.Models;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadLocalImportDialogViewModel
{
[RelayCommand]
    private void Cancel()
    {
        logger.LogInformation(
            "Canceled local import dialog. DialogState={DialogState} SelectedFileName={SelectedFileName}",
            DialogState,
            string.IsNullOrWhiteSpace(SelectedFileName) ? "<none>" : SelectedFileName);

        Close(resetDialogState: true);
    }

    [RelayCommand(CanExecute = nameof(CanConfirmImport))]
    private async Task ConfirmImportAsync()
    {
        // 先识别格式再决定是否直接导入；未知格式需要用户显式确认，不能猜测并写入实例目录。
        if (!CanConfirmImport())
            return;

        var importPath = SelectedFilePath;
        var importFileName = SelectedFileName;
        logger.LogInformation(
            "Confirmed local modpack import. SelectedFileName={SelectedFileName}",
            importFileName);
        IsImporting = true;

        try
        {
            var recognitionResult = await modpackImportService.RecognizeArchiveAsync(importPath);
            if (!recognitionResult.IsSuccess)
            {
                logger.LogInformation(
                    "Selected local import file was not recognized. SelectedFileName={SelectedFileName} FailureReason={FailureReason}",
                    importFileName,
                    recognitionResult.FailureReason);
                ExecuteOnUiThread(() => DialogState = DownloadLocalImportDialogState.Unrecognized);
                return;
            }

            DownloadTaskItem createdTask = null!;
            ExecuteOnUiThread(() =>
            {
                floatingMessageService.Show(Strings.Status_ModpackInstalling);
                createdTask = downloadTasksPage.BeginTask(Strings.Download_LocalImportTaskTitle, importFileName);
                Close(resetDialogState: true);
            });

            var importTask = RunImportTaskAsync(
                importPath,
                importFileName,
                createdTask,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond);
            downloadTasksPage.TrackBackgroundTask(importTask);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected local modpack recognition failure. SelectedFileName={SelectedFileName}",
                importFileName);
        }
        finally
        {
            ExecuteOnUiThread(() =>
            {
                IsImporting = false;
                ConfirmImportCommand.NotifyCanExecuteChanged();
            });
        }
    }
}
