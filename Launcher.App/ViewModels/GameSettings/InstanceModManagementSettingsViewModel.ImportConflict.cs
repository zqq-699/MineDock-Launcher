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
using Launcher.App.ViewModels.Shared;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceModManagementSettingsViewModel
{
    /// <summary>
    /// 将导入重名冲突转换为可绑定对话框状态，并异步等待用户选择。
    /// </summary>
    private async Task<bool> RequestModImportConflictResolutionAsync(string sourcePath, string fileName)
    {
        // RunContinuationsAsynchronously 防止按钮事件处理器同步恢复导入循环并形成 UI 重入。
        pendingImportConflictResolutionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ImportModConflictRequested?.Invoke(new ModImportConflictRequest(sourcePath, fileName));
        return await pendingImportConflictResolutionSource.Task;
    }

    private void ResolvePendingImportConflict(bool shouldReplace)
    {
        pendingImportConflictResolutionSource?.TrySetResult(shouldReplace);
        pendingImportConflictResolutionSource = null;
    }

    private bool TryValidateImportPaths(
        IReadOnlyList<string> paths,
        string invalidTypeMessage,
        out string failureMessage)
    {
        var validation = importPathValidator.Validate(paths, InstanceContentImportKind.Mod);
        failureMessage = validation.Failure is InstanceContentImportPathFailure.DirectoryNotSupported
            ? Strings.GameSettings_DropFoldersUnsupportedMessage
            : validation.IsValid ? string.Empty : invalidTypeMessage;
        return validation.IsValid;
    }

    private static string GetPathForEnabledState(string path, bool enabled)
    {
        return enabled
            ? path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                ? path[..^".disabled".Length]
                : path
            : path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                ? path
                : path + ".disabled";
    }

    private static string GetStableModPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".disabled".Length]
            : path;
    }
}
