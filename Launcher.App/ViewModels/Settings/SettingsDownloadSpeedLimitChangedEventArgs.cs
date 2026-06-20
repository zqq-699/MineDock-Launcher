namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsDownloadSpeedLimitChangedEventArgs : EventArgs
{
    public SettingsDownloadSpeedLimitChangedEventArgs(int downloadSpeedLimitMbPerSecond)
    {
        DownloadSpeedLimitMbPerSecond = downloadSpeedLimitMbPerSecond;
    }

    public int DownloadSpeedLimitMbPerSecond { get; }
}
