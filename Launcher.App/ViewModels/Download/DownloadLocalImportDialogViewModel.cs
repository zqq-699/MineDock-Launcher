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

public enum DownloadLocalImportDialogState
{
    Selection,
    Unrecognized
}

/// <summary>
/// 管理本地整合包文件选择、格式识别、未识别确认和导入进度对话框状态。
/// </summary>
public sealed partial class DownloadLocalImportDialogViewModel : ObservableObject
{
    // 文件预览只保存轻量路径状态；真正解压识别在用户确认或 Drop 完成后执行。
    private readonly IFilePickerService filePickerService;
    private readonly ILocalModpackImportService modpackImportService;
    private readonly DownloadTasksPageViewModel downloadTasksPage;
    private readonly IUiDispatcher uiDispatcher;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly DownloadModpackManualDownloadsDialogViewModel modpackManualDownloadsDialog;
    private readonly IExistingFilePathValidator existingFilePathValidator;
    private readonly ILogger<DownloadLocalImportDialogViewModel> logger;
    private DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference;
    private int downloadSpeedLimitMbPerSecond;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string selectedFilePath = string.Empty;

    [ObservableProperty]
    private string selectedFileName = string.Empty;

    [ObservableProperty]
    private bool isDragOver;

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private DownloadLocalImportDialogState dialogState = DownloadLocalImportDialogState.Selection;

    public DownloadLocalImportDialogViewModel(
        IFilePickerService filePickerService,
        ILocalModpackImportService modpackImportService,
        DownloadTasksPageViewModel downloadTasksPage,
        IUiDispatcher uiDispatcher,
        IFloatingMessageService floatingMessageService,
        DownloadModpackManualDownloadsDialogViewModel modpackManualDownloadsDialog,
        IExistingFilePathValidator existingFilePathValidator,
        ILogger<DownloadLocalImportDialogViewModel>? logger = null)
    {
        this.filePickerService = filePickerService;
        this.modpackImportService = modpackImportService;
        this.downloadTasksPage = downloadTasksPage;
        this.uiDispatcher = uiDispatcher;
        this.floatingMessageService = floatingMessageService;
        this.modpackManualDownloadsDialog = modpackManualDownloadsDialog;
        this.existingFilePathValidator = existingFilePathValidator;
        this.logger = logger ?? NullLogger<DownloadLocalImportDialogViewModel>.Instance;
    }

    public event EventHandler<GameInstance>? ModpackImported;

    public bool HasSelectedFile => !string.IsNullOrWhiteSpace(SelectedFilePath);

    public bool IsSelectionState => DialogState is DownloadLocalImportDialogState.Selection;

    public bool IsUnrecognizedState => DialogState is DownloadLocalImportDialogState.Unrecognized;

    public bool CanAcceptDroppedFiles(IReadOnlyList<string> paths)
    {
        return !IsImporting && TryResolveSingleFile(paths, out _);
    }

    public async Task<bool> ImportDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (!CanAcceptDroppedFiles(paths) || !TryResolveSingleFile(paths, out var resolvedPath))
            return false;

        Reset();
        SetSelectedFile(resolvedPath, "page-dragdrop");
        await ConfirmImportCommand.ExecuteAsync(null);
        if (DialogState is DownloadLocalImportDialogState.Unrecognized)
            IsOpen = true;

        return true;
    }

    public void Open()
    {
        Reset();
        IsOpen = true;
        logger.LogInformation("Opened local import dialog.");
    }

    public void Reset()
    {
        SelectedFilePath = string.Empty;
        SelectedFileName = string.Empty;
        IsDragOver = false;
        DialogState = DownloadLocalImportDialogState.Selection;
    }

    public bool PreviewDroppedFiles(IReadOnlyList<string> paths)
    {
        // DragOver 高频调用只验证单文件和扩展名，避免反复读取大型压缩包。
        if (IsImporting || DialogState is not DownloadLocalImportDialogState.Selection)
        {
            IsDragOver = false;
            return false;
        }

        var canAccept = TryResolveSingleFile(paths, out _);
        IsDragOver = canAccept;
        return canAccept;
    }

    public bool ApplyDroppedFiles(IReadOnlyList<string> paths)
    {
        // Drop 时规范化一次路径并更新来源提示，后续确认始终使用同一快照。
        if (IsImporting || DialogState is not DownloadLocalImportDialogState.Selection)
        {
            IsDragOver = false;
            return false;
        }

        if (!TryResolveSingleFile(paths, out var resolvedPath))
        {
            IsDragOver = false;
            return false;
        }

        SetSelectedFile(resolvedPath, "dragdrop");
        IsDragOver = false;
        return true;
    }

    public void ClearDropState()
    {
        IsDragOver = false;
    }

    public void ApplyDownloadSourcePreference(DownloadSourcePreference preference)
    {
        downloadSourcePreference = preference;
    }

    public void ApplyDownloadSpeedLimit(int value)
    {
        downloadSpeedLimitMbPerSecond = Math.Max(value, 0);
    }
}
