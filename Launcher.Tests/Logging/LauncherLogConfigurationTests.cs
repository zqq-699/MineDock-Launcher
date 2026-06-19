using Launcher.App.Logging;

namespace Launcher.Tests.Logging;

public sealed class LauncherLogConfigurationTests
{
    [Fact]
    public void ResolveLogDirectoryUsesExecutableNameBesideExecutable()
    {
        var logDirectory = LauncherLogConfiguration.ResolveLogDirectory(@"C:\Apps\Launcher.App.exe");

        Assert.Equal(@"C:\Apps\Launcher.App\log", logDirectory);
    }

    [Fact]
    public void SanitizeLauncherNameReplacesInvalidFileNameCharacters()
    {
        var sanitized = LauncherLogConfiguration.SanitizeLauncherName("Mine:Launcher*?");

        Assert.DoesNotContain(':', sanitized);
        Assert.DoesNotContain('*', sanitized);
        Assert.DoesNotContain('?', sanitized);
        Assert.Equal("Mine-Launcher--", sanitized);
    }

    [Fact]
    public void LoggingDefaultsMatchRetentionAndRolloverPolicy()
    {
        Assert.Equal(30, LauncherLogConfiguration.RetainedDays);
        Assert.Equal(20L * 1024 * 1024, LauncherLogConfiguration.FileSizeLimitBytes);
        Assert.True(LauncherLogConfiguration.RollOnFileSizeLimit);
    }
}
