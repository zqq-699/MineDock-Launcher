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
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class LanguageSettingsViewModel : SettingsSectionViewModelBase
{
    internal LanguageSettingsViewModel(SettingsPersistenceCoordinator persistence)
        : base(persistence)
    {
        LanguageOptions =
        [
            new(LauncherLanguages.SimplifiedChinese, Strings.Settings_LanguageSimplifiedChinese),
            new(LauncherLanguages.TraditionalChinese, Strings.Settings_LanguageTraditionalChinese),
            new(LauncherLanguages.Japanese, Strings.Settings_LanguageJapanese),
            new(LauncherLanguages.English, Strings.Settings_LanguageEnglish)
        ];
        selectedLanguageOption = LanguageOptions[0];
    }

    public ObservableCollection<SettingsLanguageOption> LanguageOptions { get; }
    [ObservableProperty] private SettingsLanguageOption? selectedLanguageOption;
    [ObservableProperty] private bool autoSetGameLanguageToLauncherLanguage;

    public string SelectedLanguageId => LauncherLanguages.Normalize(SelectedLanguageOption?.Id);
    public bool IsLanguageRestartNoticeVisible =>
        !string.Equals(SelectedLanguageId, CurrentLanguageId, StringComparison.OrdinalIgnoreCase);
    public string LanguageRestartNoticeText => IsLanguageRestartNoticeVisible
        ? BuildLanguageRestartNoticeText(CurrentLanguageId, SelectedLanguageId)
        : string.Empty;

    public void Load(LauncherSettings settings)
    {
        LoadState(() =>
        {
            var normalized = LauncherLanguages.Normalize(settings.LauncherLanguage);
            SelectedLanguageOption = LanguageOptions.FirstOrDefault(option =>
                string.Equals(option.Id, normalized, StringComparison.OrdinalIgnoreCase)) ?? LanguageOptions[0];
            AutoSetGameLanguageToLauncherLanguage = settings.AutoSetGameLanguageToLauncherLanguage;
        });
    }

    partial void OnSelectedLanguageOptionChanged(SettingsLanguageOption? value)
    {
        OnPropertyChanged(nameof(SelectedLanguageId));
        OnPropertyChanged(nameof(IsLanguageRestartNoticeVisible));
        OnPropertyChanged(nameof(LanguageRestartNoticeText));
        PersistLanguage();
    }

    partial void OnAutoSetGameLanguageToLauncherLanguageChanged(bool value) => PersistLanguage();

    private void PersistLanguage()
    {
        Persist(settings =>
        {
            settings.LauncherLanguage = SelectedLanguageId;
            settings.AutoSetGameLanguageToLauncherLanguage = AutoSetGameLanguageToLauncherLanguage;
        });
    }

    private static string CurrentLanguageId => LauncherLanguages.Normalize(CultureInfo.CurrentUICulture.Name);

    private static string BuildLanguageRestartNoticeText(string currentLanguageId, string targetLanguageId)
    {
        var currentNotice = Strings.GetLanguageRestartNotice(CultureInfo.GetCultureInfo(currentLanguageId));
        var targetNotice = Strings.GetLanguageRestartNotice(CultureInfo.GetCultureInfo(targetLanguageId));
        return string.Equals(currentNotice, targetNotice, StringComparison.Ordinal)
            ? currentNotice
            : $"{currentNotice}{Environment.NewLine}{targetNotice}";
    }
}

public sealed record SettingsLanguageOption(string Id, string Title);
