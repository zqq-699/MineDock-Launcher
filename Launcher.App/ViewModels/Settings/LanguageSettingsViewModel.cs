using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class LanguageSettingsViewModel : SettingsSectionViewModelBase
{
    public LanguageSettingsViewModel(SettingsPageViewModel parent)
        : base(parent)
    {
        LanguageOptions.Add(Strings.Settings_LanguageSimplifiedChinese);
        SelectedLanguageOption = LanguageOptions[0];
    }

    public ObservableCollection<string> LanguageOptions { get; } = [];

    [ObservableProperty]
    private string? selectedLanguageOption;

    public string SelectedLanguageId => ResolveLanguageId(SelectedLanguageOption);

    public void LoadSelection(string? language)
    {
        SelectedLanguageOption = ResolveLanguageTitle(language);
    }

    partial void OnSelectedLanguageOptionChanged(string? value)
    {
        Parent.NotifyLanguagePreferenceChanged();
    }

    private static string ResolveLanguageId(string? title)
    {
        return string.Equals(title, Strings.Settings_LanguageSimplifiedChinese, StringComparison.Ordinal)
            ? LauncherDefaults.DefaultLauncherLanguage
            : LauncherDefaults.DefaultLauncherLanguage;
    }

    private static string ResolveLanguageTitle(string? language)
    {
        return string.Equals(language, LauncherDefaults.DefaultLauncherLanguage, StringComparison.OrdinalIgnoreCase)
            ? Strings.Settings_LanguageSimplifiedChinese
            : Strings.Settings_LanguageSimplifiedChinese;
    }
}
