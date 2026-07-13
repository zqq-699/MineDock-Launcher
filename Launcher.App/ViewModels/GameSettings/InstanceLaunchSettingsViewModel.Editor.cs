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
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceLaunchSettingsViewModel
{
private void LoadEditorFromInstance()
    {
        // 回填必须包在抑制区间内，否则设置实例会立即把相同值再保存一次。
        suppressAutoSave = true;
        try
        {
            var mode = selectedInstance?.LaunchSettingsMode ?? LaunchSettingsMode.UseGlobal;
            SelectedLaunchSettingsModeOption = ResolveLaunchSettingsModeOption(mode);
            if (mode is LaunchSettingsMode.UseGlobal)
            {
                ApplyGlobalLaunchSettingsToEditorCore();
                return;
            }

            SelectedMemoryModeOption = ResolveMemoryModeOption(selectedInstance?.MemorySettingsMode ?? MemorySettingsMode.Manual);
            MemoryMb = NormalizeMemoryValue(selectedInstance?.MemoryMb ?? LauncherDefaults.DefaultMemoryMb);
            LaunchCheckFilesBeforeLaunchEnabled = selectedInstance?.CheckFilesBeforeLaunch ?? true;
            LaunchAutoRepairMissingFilesEnabled = selectedInstance?.AutoRepairMissingFiles ?? true;
            LaunchMinimizeLauncherAfterLaunchEnabled = selectedInstance?.MinimizeLauncherAfterLaunch ?? false;
            LaunchFullScreenEnabled = selectedInstance?.LaunchFullScreen ?? false;
            LaunchPreLaunchCommand = selectedInstance?.PreLaunchCommand ?? string.Empty;
            LaunchWaitForPreLaunchCommand = selectedInstance?.WaitForPreLaunchCommand ?? true;
            LaunchPostExitCommand = selectedInstance?.PostExitCommand ?? string.Empty;
            LaunchJvmArguments = selectedInstance?.JvmArguments ?? string.Empty;
            LaunchGameArguments = selectedInstance?.GameArguments ?? string.Empty;
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private void ApplyGlobalLaunchSettingsToEditor()
    {
        suppressAutoSave = true;
        try
        {
            ApplyGlobalLaunchSettingsToEditorCore();
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private void ApplyGlobalLaunchSettingsToEditorCore()
    {
        LaunchCheckFilesBeforeLaunchEnabled = globalSettings.DefaultCheckFilesBeforeLaunch;
        LaunchAutoRepairMissingFilesEnabled = globalSettings.DefaultAutoRepairMissingFiles;
        LaunchMinimizeLauncherAfterLaunchEnabled = globalSettings.DefaultMinimizeLauncherAfterLaunch;
        LaunchFullScreenEnabled = globalSettings.DefaultLaunchFullScreen;
        LaunchPreLaunchCommand = globalSettings.DefaultPreLaunchCommand;
        LaunchWaitForPreLaunchCommand = globalSettings.DefaultWaitForPreLaunchCommand;
        LaunchPostExitCommand = globalSettings.DefaultPostExitCommand;
        LaunchJvmArguments = globalSettings.DefaultJvmArguments;
        LaunchGameArguments = globalSettings.DefaultGameArguments;
        SelectedMemoryModeOption = ResolveMemoryModeOption(globalSettings.DefaultMemorySettingsMode);
        MemoryMb = NormalizeMemoryValue(globalSettings.DefaultMemoryMb);
    }

    private void ApplyLaunchCheckDependency(bool checkFilesBeforeLaunch)
    {
        if (LaunchAutoRepairMissingFilesEnabled == checkFilesBeforeLaunch)
            return;

        // 自动修复依赖启动前检查。关闭检查时同步关闭修复，重新开启时恢复成可用的默认组合。
        suppressAutoSave = true;
        try
        {
            LaunchAutoRepairMissingFilesEnabled = checkFilesBeforeLaunch;
        }
        finally
        {
            suppressAutoSave = false;
        }
    }
}
