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

using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed class JavaSettingsViewModel : SettingsSectionViewModelBase
{
    internal JavaSettingsViewModel(
        SettingsPersistenceCoordinator persistence,
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IStatusService statusService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService,
        Func<string> minecraftDirectoryProvider)
        : base(persistence)
    {
        Editor = new JavaSettingsEditorViewModel(
            javaRuntimeDiscoveryService,
            statusService,
            filePickerService,
            floatingMessageService,
            minecraftDirectoryProvider);
        Editor.JavaSelectionChanged += Editor_JavaSelectionChanged;
    }

    public event EventHandler? LaunchDefaultsChanged;

    public JavaSettingsEditorViewModel Editor { get; }

    public void Load(LauncherSettings settings)
    {
        LoadState(() => Editor.LoadSelection(settings.JavaSelectionMode, settings.SelectedJavaExecutablePath));
        _ = RefreshForDisplayAsync();
    }

    private async Task RefreshForDisplayAsync()
    {
        try
        {
            await Editor.RefreshJavaRuntimesForDisplayAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Editor_JavaSelectionChanged(object? sender, EventArgs e)
    {
        if (!CanPersist)
            return;

        Persist(settings =>
        {
            settings.JavaSelectionMode = Editor.SelectedMode;
            settings.SelectedJavaExecutablePath = Editor.SelectedMode is JavaSelectionMode.Manual
                && !string.IsNullOrWhiteSpace(Editor.SelectedExecutablePath)
                ? Editor.SelectedExecutablePath
                : null;
        });
        LaunchDefaultsChanged?.Invoke(this, EventArgs.Empty);
    }
}
