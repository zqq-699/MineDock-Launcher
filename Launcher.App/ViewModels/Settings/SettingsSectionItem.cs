using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class SettingsSectionItem : ObservableObject
{
    public SettingsSectionItem(SettingsPageSection section, string title, string iconKey)
    {
        Section = section;
        Title = title;
        IconKey = iconKey;
    }

    public SettingsPageSection Section { get; }

    public string Title { get; }

    public string IconKey { get; }

    [ObservableProperty]
    private bool isSelected;
}
