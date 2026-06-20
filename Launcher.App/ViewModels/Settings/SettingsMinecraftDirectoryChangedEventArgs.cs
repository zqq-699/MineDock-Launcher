namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsMinecraftDirectoryChangedEventArgs : EventArgs
{
    public SettingsMinecraftDirectoryChangedEventArgs(string minecraftDirectory)
    {
        MinecraftDirectory = minecraftDirectory;
    }

    public string MinecraftDirectory { get; }
}
