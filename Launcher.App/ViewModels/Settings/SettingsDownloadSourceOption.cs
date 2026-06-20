using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsDownloadSourceOption
{
    public SettingsDownloadSourceOption(DownloadSourcePreference preference, string title)
    {
        Preference = preference;
        Title = title;
    }

    public DownloadSourcePreference Preference { get; }

    public string Title { get; }
}
