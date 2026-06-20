using System.IO;
using Launcher.Application;

namespace Launcher.Infrastructure;

public sealed class LauncherPathProvider
{
    private readonly string applicationDataDirectory;

    public LauncherPathProvider(string? applicationDataDirectory = null)
    {
        this.applicationDataDirectory = string.IsNullOrWhiteSpace(applicationDataDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.GetFullPath(applicationDataDirectory);
    }

    public string ApplicationId => LauncherApplicationIdentity.StorageDirectoryName;

    public string DefaultDataDirectory =>
        Path.Combine(applicationDataDirectory, ApplicationId);

    public string DefaultMinecraftDirectory =>
        Path.Combine(AppContext.BaseDirectory, ".minecraft");
}
