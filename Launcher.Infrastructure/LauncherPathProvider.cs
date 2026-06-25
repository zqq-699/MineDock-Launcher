using System.IO;
using Launcher.Application;

namespace Launcher.Infrastructure;

public sealed class LauncherPathProvider
{
    private readonly string applicationBaseDirectory;
    private readonly string roamingApplicationDataDirectory;

    public LauncherPathProvider(string? applicationBaseDirectory = null, string? applicationDataDirectory = null)
    {
        this.applicationBaseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(applicationBaseDirectory);
        roamingApplicationDataDirectory = string.IsNullOrWhiteSpace(applicationDataDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.GetFullPath(applicationDataDirectory);
    }

    public string ApplicationId => LauncherApplicationIdentity.StorageDirectoryName;

    public string DefaultDataDirectory =>
        Path.Combine(applicationBaseDirectory, ApplicationId);

    public string DefaultAccountDataDirectory =>
        Path.Combine(roamingApplicationDataDirectory, ApplicationId, "accounts");

    public string DefaultMinecraftDirectory =>
        Path.Combine(AppContext.BaseDirectory, ".minecraft");
}
