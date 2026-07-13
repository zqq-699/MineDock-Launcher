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
    [RelayCommand]
    private void OpenModFolder()
    {
        if (selectedInstance is null)
            return;

        try
        {
            var modsDirectory = instanceFolderService.EnsureDirectoryExists(
                Path.Combine(selectedInstance.InstanceDirectory, "mods"));
            logger.LogInformation(
                "Opening mod folder. InstanceId={InstanceId} ModsDirectory={ModsDirectory}",
                selectedInstance.Id,
                modsDirectory);

            if (!instanceFolderService.TryOpen(modsDirectory))
            {
                logger.LogWarning(
                    "Failed to open mod folder. InstanceId={InstanceId} ModsDirectory={ModsDirectory}",
                    selectedInstance.Id,
                    modsDirectory);
                statusService.Report(Strings.Status_OpenInstanceFolderFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare mod folder for opening. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
        }
    }

    [RelayCommand]
    private async Task ImportLocalModAsync()
    {
        if (selectedInstance is null)
            return;

        var modPath = filePickerService.PickModFile();
        if (string.IsNullOrWhiteSpace(modPath))
            return;

        await ImportModFilesAsync([modPath], ImportTriggerSource.FilePicker);
    }

    public Task ReplaceImportedModAsync(string sourcePath)
    {
        ResolvePendingImportConflict(true);
        return Task.CompletedTask;
    }

    public void SkipPendingImportedModReplacement()
    {
        ResolvePendingImportConflict(false);
    }

    public GameSettingsFileDropEvaluation EvaluateDroppedFiles(IReadOnlyList<string> paths)
    {
        if (!IsModManagementSupported)
            return GameSettingsFileDropEvaluation.Reject(ModUnavailableMessage);

        return TryValidateImportPaths(paths, Strings.GameSettings_DropModsOnlyMessage, out var failureMessage)
            ? GameSettingsFileDropEvaluation.Accept(Strings.GameSettings_DropImportModsMessage)
            : GameSettingsFileDropEvaluation.Reject(failureMessage);
    }

    public Task ImportDroppedModFilesAsync(IReadOnlyList<string> paths)
    {
        return ImportModFilesAsync(paths, ImportTriggerSource.DragDrop);
    }

    private async Task ImportLocalModCoreAsync(string modPath, bool overwriteExisting)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(modPath))
            return;

        logger.LogInformation(
            "Importing local mod from file picker. InstanceId={InstanceId} SourcePath={SourcePath} OverwriteExisting={OverwriteExisting}",
            selectedInstance.Id,
            modPath,
            overwriteExisting);

        try
        {
            await localModsViewModel.ImportModFromPathAsync(modPath, overwriteExisting);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to import local mod. InstanceId={InstanceId} SourcePath={SourcePath} OverwriteExisting={OverwriteExisting}",
                selectedInstance.Id,
                modPath,
                overwriteExisting);
            statusService.Report(Strings.Status_LocalModImportFailed);
        }
    }

    /// <summary>
    /// 校验并顺序导入一组 Mod 文件，逐项处理重名冲突并汇总部分失败结果。
    /// </summary>
    private async Task ImportModFilesAsync(IReadOnlyList<string> paths, ImportTriggerSource source)
    {
        if (selectedInstance is null)
            return;

        // 先对整个批次做类型/目录校验，避免导入一半后才发现输入中混有非法路径。
        if (!TryValidateImportPaths(paths, Strings.GameSettings_DropModsOnlyMessage, out var validationMessage))
        {
            statusService.Report(source is ImportTriggerSource.DragDrop
                ? validationMessage
                : Strings.Status_LocalModImportFailed);
            return;
        }

        logger.LogInformation(
            "Starting local mod import batch. InstanceId={InstanceId} Source={Source} FileCount={FileCount}",
            selectedInstance.Id,
            source,
            paths.Count);

        // 顺序处理是有意的：每个重名文件都可能需要等待独立的用户确认。
        var successCount = 0;
        foreach (var modPath in paths)
        {
            var fileName = Path.GetFileName(modPath);
            var overwriteExisting = false;
            if (localModsViewModel.Mods.Any(mod => string.Equals(mod.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                // 冲突判断基于当前快照；确认替换后由服务再次负责安全覆盖。
                var replace = await RequestModImportConflictResolutionAsync(modPath, fileName);
                if (!replace)
                {
                    logger.LogInformation(
                        "Skipping local mod replacement after user canceled conflict dialog. InstanceId={InstanceId} SourcePath={SourcePath}",
                        selectedInstance.Id,
                        modPath);
                    continue;
                }

                overwriteExisting = true;
            }

            try
            {
                var imported = await localModsViewModel.ImportModFromPathAsync(modPath, overwriteExisting, reportStatus: false);
                // 返回 false 是可预期业务失败；停止批次以免连续产生相同失败和提示。
                if (!imported)
                {
                    statusService.Report(Strings.Status_LocalModImportFailed);
                    return;
                }

                successCount++;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to import local mod during batch import. InstanceId={InstanceId} SourcePath={SourcePath} OverwriteExisting={OverwriteExisting}",
                    selectedInstance.Id,
                    modPath,
                    overwriteExisting);
                statusService.Report(Strings.Status_LocalModImportFailed);
                return;
            }
        }

        // 单个文件状态在导入期间不重复上报，只在批次结束时给出汇总。
        if (successCount > 0)
        {
            statusService.Report(successCount == 1
                ? Strings.Status_LocalModImported
                : string.Format(Strings.Status_LocalModsImportedFormat, successCount));
        }
    }
}
