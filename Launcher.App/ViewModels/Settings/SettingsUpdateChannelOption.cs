using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsUpdateChannelOption
{
    public SettingsUpdateChannelOption(LauncherUpdateChannel channel, string title)
    {
        Channel = channel;
        Title = title;
    }

    public LauncherUpdateChannel Channel { get; }

    public string Title { get; }
}
