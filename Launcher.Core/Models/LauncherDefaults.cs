namespace Launcher.Core.Models;

public static class LauncherDefaults
{
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher");
}
