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
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDetailsViewModel
{
public GameSettingsFileDropEvaluation EvaluateImportDrop(IReadOnlyList<string> paths)
    {
        // 由当前分区决定可接受类型，聚合层保证拖放不会路由到隐藏页面。
        if (SelectedInstance is null)
            return GameSettingsFileDropEvaluation.Hidden;

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.EvaluateDroppedFiles(paths),
            "saves" => SaveManagement.EvaluateDroppedFiles(paths),
            "resource_packs" => ResourcePackManagement.EvaluateDroppedFiles(paths),
            "shaders" => ShaderPackManagement.EvaluateDroppedFiles(paths),
            _ => GameSettingsFileDropEvaluation.Hidden
        };
    }

    public Task HandleImportDropAsync(IReadOnlyList<string> paths)
    {
        // 实际文件操作仍由对应分区服务完成，此处只选择目标流程。
        if (SelectedInstance is null)
            return Task.CompletedTask;

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.ImportDroppedModFilesAsync(paths),
            "saves" => SaveManagement.ImportDroppedSaveArchivesAsync(paths),
            "resource_packs" => ResourcePackManagement.ImportDroppedResourcePackArchivesAsync(paths),
            "shaders" => ShaderPackManagement.ImportDroppedShaderPackArchivesAsync(paths),
            _ => Task.CompletedTask
        };
    }

    public void ResolvePendingModImportConflict(bool shouldReplace)
    {
        if (shouldReplace)
            ModManagement.ReplaceImportedModAsync(string.Empty);
        else
            ModManagement.SkipPendingImportedModReplacement();
    }

    public Task ReplaceImportedModAsync(string sourcePath)
    {
        return ModManagement.ReplaceImportedModAsync(sourcePath);
    }
}
