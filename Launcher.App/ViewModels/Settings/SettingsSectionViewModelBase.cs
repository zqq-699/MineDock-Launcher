using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Settings;

public abstract class SettingsSectionViewModelBase : ObservableObject
{
    protected SettingsSectionViewModelBase(SettingsPageViewModel parent)
    {
        Parent = parent;
    }

    public SettingsPageViewModel Parent { get; }
}
