using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsDownloadSourceChangedEventArgs : EventArgs
{
    public SettingsDownloadSourceChangedEventArgs(DownloadSourcePreference preference)
    {
        Preference = preference;
    }

    public DownloadSourcePreference Preference { get; }
}
