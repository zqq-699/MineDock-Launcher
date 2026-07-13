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
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class ThemeSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly IThemeService themeService;

    internal ThemeSettingsViewModel(SettingsPersistenceCoordinator persistence, IThemeService themeService)
        : base(persistence)
    {
        this.themeService = themeService;
        ThemeOptions =
        [
            new(LauncherDefaults.DefaultTheme, Strings.Settings_ThemeDarkTitle),
            new("Light", Strings.Settings_ThemeLightTitle)
        ];
        AccentColorOptions =
        [
            new(LauncherAccentColors.Blue, Strings.Settings_AccentColorBlueTitle),
            new(LauncherAccentColors.Cyan, Strings.Settings_AccentColorCyanTitle),
            new(LauncherAccentColors.Green, Strings.Settings_AccentColorGreenTitle),
            new(LauncherAccentColors.Emerald, Strings.Settings_AccentColorEmeraldTitle),
            new(LauncherAccentColors.Purple, Strings.Settings_AccentColorPurpleTitle),
            new(LauncherAccentColors.Pink, Strings.Settings_AccentColorPinkTitle),
            new(LauncherAccentColors.Orange, Strings.Settings_AccentColorOrangeTitle),
            new(LauncherAccentColors.Amber, Strings.Settings_AccentColorAmberTitle)
        ];
        selectedThemeOption = ThemeOptions[0];
        selectedAccentColorOption = AccentColorOptions[0];
    }

    public ObservableCollection<SettingsThemeOption> ThemeOptions { get; }
    public ObservableCollection<SettingsAccentColorOption> AccentColorOptions { get; }

    [ObservableProperty] private SettingsThemeOption? selectedThemeOption;
    [ObservableProperty] private SettingsAccentColorOption? selectedAccentColorOption;
    [ObservableProperty] private bool followSystemTheme = true;
    [ObservableProperty] private int launcherBackgroundOpacityPercent = LauncherDefaults.DefaultLauncherBackgroundOpacityPercent;
    [ObservableProperty] private bool disableBackgroundBlur;

    public bool IsThemeSelectionVisible => !FollowSystemTheme;
    public string LauncherBackgroundOpacityText => $"{LauncherBackgroundOpacityPercent}%";

    public void Load(LauncherSettings settings)
    {
        LoadState(() =>
        {
            FollowSystemTheme = settings.ThemeFollowSystem;
            SelectedThemeOption = ThemeOptions.FirstOrDefault(option =>
                string.Equals(option.Id, settings.Theme, StringComparison.OrdinalIgnoreCase)) ?? ThemeOptions[0];
            SelectedAccentColorOption = AccentColorOptions.FirstOrDefault(option =>
                string.Equals(option.Id, settings.AccentColor, StringComparison.OrdinalIgnoreCase)) ?? AccentColorOptions[0];
            DisableBackgroundBlur = settings.DisableBackgroundBlur;
            LauncherBackgroundOpacityPercent = Math.Clamp(settings.LauncherBackgroundOpacityPercent, 0, 100);
        });
    }

    partial void OnFollowSystemThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsThemeSelectionVisible));
        ApplyPreferenceAndPersist();
    }

    partial void OnSelectedThemeOptionChanged(SettingsThemeOption? oldValue, SettingsThemeOption? newValue)
    {
        if (newValue is null)
        {
            LoadState(() => SelectedThemeOption = oldValue ?? ThemeOptions[0]);
            return;
        }

        ApplyPreferenceAndPersist();
    }

    partial void OnSelectedAccentColorOptionChanged(
        SettingsAccentColorOption? oldValue,
        SettingsAccentColorOption? newValue)
    {
        if (newValue is null)
        {
            LoadState(() => SelectedAccentColorOption = oldValue ?? AccentColorOptions[0]);
            return;
        }

        if (!CanPersist)
            return;
        var accent = newValue.Id;
        themeService.ApplyAccent(accent);
        Persist(settings => settings.AccentColor = accent);
    }

    partial void OnLauncherBackgroundOpacityPercentChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 100);
        if (value != normalized)
        {
            LauncherBackgroundOpacityPercent = normalized;
            return;
        }
        OnPropertyChanged(nameof(LauncherBackgroundOpacityText));
        if (!CanPersist)
            return;
        themeService.ApplyBackgroundOpacity(normalized);
        Persist(settings => settings.LauncherBackgroundOpacityPercent = normalized);
    }

    partial void OnDisableBackgroundBlurChanged(bool value)
    {
        if (!CanPersist)
            return;
        themeService.ApplyBackgroundBlurDisabled(value);
        Persist(settings => settings.DisableBackgroundBlur = value);
    }

    private void ApplyPreferenceAndPersist()
    {
        if (!CanPersist || SelectedThemeOption is null)
            return;
        var theme = SelectedThemeOption.Id;
        themeService.ApplyPreference(theme, FollowSystemTheme, LauncherBackgroundOpacityPercent, DisableBackgroundBlur);
        Persist(settings =>
        {
            settings.Theme = theme;
            settings.ThemeFollowSystem = FollowSystemTheme;
        });
    }
}
