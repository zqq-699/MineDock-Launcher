namespace Launcher.Domain.Models;

public static class LauncherDefaults
{
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher");

    public static string DefaultMinecraftDirectory =>
        Path.Combine(AppContext.BaseDirectory, ".minecraft");
}
