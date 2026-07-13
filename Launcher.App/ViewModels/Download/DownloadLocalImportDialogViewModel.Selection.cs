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
    private void SelectFile()
    {
        var filePath = filePickerService.PickLocalImportFile();
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        if (!TryResolveSingleFile([filePath], out _))
            return;

        SetSelectedFile(filePath, "picker");
    }

    partial void OnSelectedFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        RefreshConfirmImportCanExecute();
    }

    partial void OnIsImportingChanged(bool value)
    {
        RefreshConfirmImportCanExecute();
    }

    partial void OnDialogStateChanged(DownloadLocalImportDialogState value)
    {
        OnPropertyChanged(nameof(IsSelectionState));
        OnPropertyChanged(nameof(IsUnrecognizedState));
        RefreshConfirmImportCanExecute();
    }

    private void SetSelectedFile(string path, string source)
    {
        // 选择新文件会清除上一文件的识别结果，防止确认按钮沿用旧格式判断。
        var normalizedPath = Path.GetFullPath(path);
        SelectedFilePath = normalizedPath;
        SelectedFileName = Path.GetFileName(normalizedPath);
        DialogState = DownloadLocalImportDialogState.Selection;
        logger.LogInformation(
            "Selected local import file. Source={Source} SelectedFileName={SelectedFileName}",
            source,
            SelectedFileName);
    }

    private bool CanConfirmImport()
    {
        return !IsImporting
            && DialogState is DownloadLocalImportDialogState.Selection
            && HasSelectedFile;
    }

    private void RefreshConfirmImportCanExecute()
    {
        ExecuteOnUiThread(() => ConfirmImportCommand.NotifyCanExecuteChanged());
    }

    private void ExecuteOnUiThread(Action action)
    {
        // 导入服务可在线程池报告进度，所有 ObservableProperty 更新统一切回 UI 线程。
        if (uiDispatcher.HasAccess)
        {
            action();
            return;
        }

        uiDispatcher.Invoke(action);
    }

    private void Close(bool resetDialogState)
    {
        // 导入进行中拒绝关闭；正常关闭可选择保留结果页或完全重置下一轮会话。
        IsOpen = false;
        SelectedFilePath = string.Empty;
        SelectedFileName = string.Empty;
        IsDragOver = false;
        if (resetDialogState)
            DialogState = DownloadLocalImportDialogState.Selection;
    }
}
