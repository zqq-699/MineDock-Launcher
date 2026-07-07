using Launcher.Application;
using Launcher.App.Logging;

namespace Launcher.Tests.Logging;

public sealed class LauncherLogConfigurationTests
{
    [Fact]
    public void ResolveLogDirectoryUsesLauncherDataFolder()
    {
        var logDirectory = LauncherLogConfiguration.ResolveLogDirectory();
        var expectedDirectory = Path.Combine(
            AppContext.BaseDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "log");

        Assert.Equal(expectedDirectory, logDirectory);
    }

    [Fact]
    public void LoggingDefaultsMatchRetentionAndRolloverPolicy()
    {
        Assert.Equal(30, LauncherLogConfiguration.RetainedDays);
        Assert.Equal(20L * 1024 * 1024, LauncherLogConfiguration.FileSizeLimitBytes);
        Assert.True(LauncherLogConfiguration.RollOnFileSizeLimit);
        Assert.Equal("mdl-.log", LauncherLogConfiguration.LogFileNamePattern);
    }
}
