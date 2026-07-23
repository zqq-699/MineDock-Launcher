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
private void ScheduleSaveUnlessSuppressed()
    {
        if (!suppressAutoSave)
            ScheduleSave();
    }

    private void ScheduleSave()
    {
        var instance = selectedInstance;
        var selectedMode = SelectedLaunchSettingsModeOption;
        if (instance is null || selectedMode is null)
            return;

        // 在调度前捕获完整快照，延迟执行时不读取可能已经切换到另一实例的 UI 属性。
        var mode = selectedMode.Mode;
        if (mode is LaunchSettingsMode.PerInstance && SelectedMemoryModeOption is null)
            return;
        var checkFilesBeforeLaunch = LaunchCheckFilesBeforeLaunchEnabled;
        var autoRepairMissingFiles = LaunchAutoRepairMissingFilesEnabled;
        var minimizeLauncherAfterLaunch = LaunchMinimizeLauncherAfterLaunchEnabled;
        var launchFullScreen = LaunchFullScreenEnabled;
        var autoJoinServerAddress = NormalizeSettingText(LaunchAutoJoinServerAddress);
        var preLaunchCommand = NormalizeSettingText(LaunchPreLaunchCommand);
        var waitForPreLaunchCommand = LaunchWaitForPreLaunchCommand;
        var postExitCommand = NormalizeSettingText(LaunchPostExitCommand);
        var jvmArguments = NormalizeSettingText(LaunchJvmArguments);
        var gameArguments = NormalizeSettingText(LaunchGameArguments);
        var memorySettingsMode = mode is LaunchSettingsMode.UseGlobal
            ? globalSettings.DefaultMemorySettingsMode
            : SelectedMemoryModeOption!.Mode;
        var memory = mode is LaunchSettingsMode.UseGlobal
            ? NormalizeMemoryValue(globalSettings.DefaultMemoryMb)
            : NormalizeMemoryValue(MemoryMb);

        // PersistenceCoordinator 按实例和区域合并写入，并在保存失败时调用回填动作恢复 UI。
        persistence.Schedule(
            "launch",
            instance,
            target => ApplyMutation(
                target,
                mode,
                checkFilesBeforeLaunch,
                autoRepairMissingFiles,
                minimizeLauncherAfterLaunch,
                launchFullScreen,
                autoJoinServerAddress,
                preLaunchCommand,
                waitForPreLaunchCommand,
                postExitCommand,
                jvmArguments,
                gameArguments,
                memorySettingsMode,
                memory),
            LoadEditorFromInstance,
            SaveMergeDelay);
    }

    private static Action? ApplyMutation(
        GameInstance instance,
        LaunchSettingsMode mode,
        bool checkFilesBeforeLaunch,
        bool autoRepairMissingFiles,
        bool minimizeLauncherAfterLaunch,
        bool launchFullScreen,
        string autoJoinServerAddress,
        string preLaunchCommand,
        bool waitForPreLaunchCommand,
        string postExitCommand,
        string jvmArguments,
        string gameArguments,
        MemorySettingsMode memorySettingsMode,
        int memoryMb)
    {
        // 先比较规范化快照，未发生语义变化时不制造磁盘写入；发生变化则返回完整回滚闭包。
        var original = InstanceLaunchSettingsSnapshot.Capture(instance);
        if (original.Matches(
                mode,
                checkFilesBeforeLaunch,
                autoRepairMissingFiles,
                minimizeLauncherAfterLaunch,
                launchFullScreen,
                autoJoinServerAddress,
                preLaunchCommand,
                waitForPreLaunchCommand,
                postExitCommand,
                jvmArguments,
                gameArguments,
                memorySettingsMode,
                memoryMb))
        {
            return null;
        }

        instance.LaunchSettingsMode = mode;
        instance.CheckFilesBeforeLaunch = checkFilesBeforeLaunch;
        instance.AutoRepairMissingFiles = autoRepairMissingFiles;
        instance.MinimizeLauncherAfterLaunch = minimizeLauncherAfterLaunch;
        instance.LaunchFullScreen = launchFullScreen;
        instance.AutoJoinServerAddress = autoJoinServerAddress;
        instance.PreLaunchCommand = preLaunchCommand;
        instance.WaitForPreLaunchCommand = waitForPreLaunchCommand;
        instance.PostExitCommand = postExitCommand;
        instance.JvmArguments = jvmArguments;
        instance.GameArguments = gameArguments;
        instance.MemorySettingsMode = memorySettingsMode;
        instance.MemoryMb = memoryMb;
        return () => original.Restore(instance);
    }
}
