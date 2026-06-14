using System.IO;

namespace Launcher.Infrastructure;

public sealed class LauncherPathProvider
{
    public string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher");

    public string DefaultMinecraftDirectory =>
        Path.Combine(AppContext.BaseDirectory, ".minecraft");
}
