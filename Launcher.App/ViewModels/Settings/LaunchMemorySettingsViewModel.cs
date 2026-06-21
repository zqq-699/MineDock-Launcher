namespace Launcher.App.ViewModels.Settings;

public sealed class LaunchMemorySettingsViewModel : SettingsSectionViewModelBase
{
    public LaunchMemorySettingsViewModel(SettingsPageViewModel parent)
        : base(parent)
    {
    }

    public void RefreshSystemMemorySnapshot()
    {
        Parent.RefreshSystemMemorySnapshot();
    }
}
