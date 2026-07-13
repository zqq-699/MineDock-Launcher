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
    private void InstallOnlineMod()
    {
        if (selectedInstance is null || !IsModManagementSupported)
            return;

        logger.LogInformation(
            "Online mod install requested from instance mod management. InstanceId={InstanceId}, MinecraftVersion={MinecraftVersion}, Loader={Loader}",
            selectedInstance.Id,
            selectedInstance.MinecraftVersion,
            selectedInstance.Loader);
        OnlineModInstallRequested?.Invoke(selectedInstance);
    }

    [RelayCommand]
    private void SetModFilter(ModManagementFilter filter)
    {
        ModFilter = filter;
    }

    [RelayCommand]
    private void ToggleMultiSelectMode()
    {
        if (IsMultiSelectMode)
        {
            ExitMultiSelectMode();
            return;
        }

        EnterMultiSelectMode();
    }

    [RelayCommand(CanExecute = nameof(CanToggleSelectAllMods))]
    private void SelectAllMods()
    {
        if (AreAllVisibleModsSelected)
        {
            foreach (var mod in Mods)
                mod.IsSelected = false;

            selectedModPaths.Clear();
            SelectedMod = null;
            UpdateSelectedModState();
            return;
        }

        foreach (var mod in Mods)
        {
            mod.IsSelected = true;
            selectedModPaths.Add(mod.FullPath);
        }

        SelectedMod = null;
        UpdateSelectedModState();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private async Task EnableSelectedModsAsync()
    {
        await SetSelectedModsEnabledAsync(enabled: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private async Task DisableSelectedModsAsync()
    {
        await SetSelectedModsEnabledAsync(enabled: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMods))]
    private void RequestDeleteSelectedMods()
    {
        var selectedMods = GetSelectedVisibleMods();
        if (selectedMods.Count == 0)
            return;

        DeleteModsRequested?.Invoke(new ModDeleteRequest(
            selectedMods.Select(mod => mod.FullPath).ToArray(),
            selectedMods.Select(mod => mod.Title).ToArray()));
    }

    [RelayCommand]
    private async Task ToggleModEnabledAsync(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
            return;

        var localMod = ResolveLocalMod(mod.FullPath);
        if (localMod is null)
            return;

        var nextPath = GetPathForEnabledState(localMod.FullPath, !localMod.IsEnabled);
        var previousSelectedPath = lastSingleSelectedModPath;
        var wasSelectedInMultiSelect = selectedModPaths.Contains(localMod.FullPath);

        if (IsMultiSelectMode)
        {
            selectedModPaths.Remove(localMod.FullPath);
            if (wasSelectedInMultiSelect)
                selectedModPaths.Add(nextPath);
        }
        else
        {
            lastSingleSelectedModPath = nextPath;
        }

        logger.LogInformation(
            "Toggling local mod enabled state. InstanceId={InstanceId} Path={Path} Enabled={Enabled}",
            selectedInstance?.Id ?? "<none>",
            localMod.FullPath,
            !localMod.IsEnabled);

        try
        {
            suppressLocalCollectionEvents = true;
            try
            {
                await localModsViewModel.ToggleModAsync(localMod);
            }
            finally
            {
                suppressLocalCollectionEvents = false;
            }

            RefreshFromLocalMods();
        }
        catch (Exception exception)
        {
            suppressLocalCollectionEvents = false;
            logger.LogError(
                exception,
                "Failed to toggle local mod enabled state. InstanceId={InstanceId} Path={Path}",
                selectedInstance?.Id ?? "<none>",
                localMod.FullPath);
            statusService.Report(localMod.IsEnabled
                ? Strings.Status_SelectedModsDisableFailed
                : Strings.Status_SelectedModsEnableFailed);

            if (IsMultiSelectMode)
            {
                selectedModPaths.Remove(nextPath);
                if (wasSelectedInMultiSelect)
                    selectedModPaths.Add(localMod.FullPath);
            }
            else
            {
                lastSingleSelectedModPath = previousSelectedPath;
            }

            RefreshFromLocalMods();
        }
    }

    [RelayCommand]
    private void OpenModFileLocation(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
            return;

        try
        {
            if (!instanceFolderService.TryRevealFile(mod.FullPath))
            {
                logger.LogWarning(
                    "Failed to reveal local mod file. InstanceId={InstanceId} Path={Path}",
                    selectedInstance?.Id ?? "<none>",
                    mod.FullPath);
                statusService.Report(Strings.Status_OpenModFileLocationFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to reveal local mod file. InstanceId={InstanceId} Path={Path}",
                selectedInstance?.Id ?? "<none>",
                mod.FullPath);
            statusService.Report(Strings.Status_OpenModFileLocationFailed);
        }
    }

    [RelayCommand]
    private void RequestDeleteMod(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
            return;

        DeleteModsRequested?.Invoke(new ModDeleteRequest(
            [mod.FullPath],
            [mod.Title]));
    }

    [RelayCommand]
    private void SelectMod(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
        {
            SelectedMod = null;
            if (IsMultiSelectMode)
                selectedModPaths.Clear();
            foreach (var item in Mods)
                item.IsSelected = false;
            UpdateSelectedModState();
            return;
        }

        if (IsMultiSelectMode)
        {
            var isSelected = !mod.IsSelected;
            mod.IsSelected = isSelected;
            if (isSelected)
                selectedModPaths.Add(mod.FullPath);
            else
                selectedModPaths.Remove(mod.FullPath);

            SelectedMod = null;
            UpdateSelectedModState();
            return;
        }

        SelectedMod = mod;
        lastSingleSelectedModPath = mod.FullPath;
        foreach (var item in Mods)
            item.IsSelected = false;
    }

    /// <summary>
    /// 删除路径对应的 Mod；批量操作允许部分失败，并在结束后统一刷新快照和选择状态。
    /// </summary>
    public async Task DeleteModsAsync(IReadOnlyList<string> fullPaths)
    {
        ArgumentNullException.ThrowIfNull(fullPaths);

        // 根据当前快照解析路径，自动忽略筛选/刷新后已失效的选择。
        var modsToDelete = ResolveLocalMods(fullPaths);
        if (modsToDelete.Count == 0)
        {
            ExitMultiSelectMode();
            return;
        }

        logger.LogInformation(
            "Deleting selected mods. InstanceId={InstanceId} Count={Count}",
            selectedInstance?.Id ?? "<none>",
            modsToDelete.Count);
        try
        {
            // 底层逐项删除并返回失败数量，允许用户保留已成功删除的结果。
            var failedCount = await localModsViewModel.DeleteModsAsync(modsToDelete);
            ExitMultiSelectMode();
            ReportBatchOperationResult(
                modsToDelete.Count,
                failedCount,
                Strings.Status_SelectedModsDeletedFormat,
                Strings.Status_SelectedModsDeletePartialFailedFormat);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to delete selected mods. InstanceId={InstanceId}",
                selectedInstance?.Id ?? "<none>");
            statusService.Report(Strings.Status_SelectedModsDeleteFailed);
        }
    }

    partial void OnInstalledModCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
        RaiseAvailabilityPropertyChanges();
        OnPropertyChanged(nameof(ModEmptyMessage));
    }

    partial void OnEnabledModCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
    }

    partial void OnModSearchQueryChanged(string value)
    {
        RefreshFromLocalMods();
        OnPropertyChanged(nameof(ModEmptyMessage));
    }

    partial void OnModFilterChanged(ModManagementFilter value)
    {
        RefreshFromLocalMods();
        OnPropertyChanged(nameof(IsAllModsFilterSelected));
        OnPropertyChanged(nameof(IsEnabledModsFilterSelected));
        OnPropertyChanged(nameof(IsDisabledModsFilterSelected));
        OnPropertyChanged(nameof(ModEmptyMessage));
    }

    partial void OnSelectedModCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedMods));
        OnPropertyChanged(nameof(AreAllVisibleModsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllModsCommand.NotifyCanExecuteChanged();
        EnableSelectedModsCommand.NotifyCanExecuteChanged();
        DisableSelectedModsCommand.NotifyCanExecuteChanged();
        RequestDeleteSelectedModsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsMultiSelectModeChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisibleModsSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
        SelectAllModsCommand.NotifyCanExecuteChanged();
    }
}
