using Launcher.Application;
using Launcher.Infrastructure;

namespace Launcher.Tests.Infrastructure;

public sealed class LauncherPathProviderTests : TestTempDirectory
{
    [Fact]
    public void DefaultDataDirectoryUsesFixedApplicationIdBesideLauncher()
    {
        var pathProvider = new LauncherPathProvider(applicationBaseDirectory: TempRoot);

        Assert.Equal("BHL", LauncherApplicationIdentity.StorageDirectoryName);
        Assert.Equal(Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName), pathProvider.DefaultDataDirectory);
        Assert.Equal(LauncherApplicationIdentity.StorageDirectoryName, pathProvider.ApplicationId);
    }

    [Fact]
    public void DefaultDataDirectoryDoesNotDependOnExecutableName()
    {
        var firstProvider = new LauncherPathProvider(applicationBaseDirectory: TempRoot);
        var secondProvider = new LauncherPathProvider(applicationBaseDirectory: TempRoot);

        Assert.Equal(firstProvider.DefaultDataDirectory, secondProvider.DefaultDataDirectory);
        Assert.Equal(Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName), firstProvider.DefaultDataDirectory);
    }

    [Fact]
    public void DefaultAccountDataDirectoryUsesApplicationDataFolder()
    {
        var appDataRoot = Path.Combine(TempRoot, "roaming");
        var pathProvider = new LauncherPathProvider(
            applicationBaseDirectory: Path.Combine(TempRoot, "app"),
            applicationDataDirectory: appDataRoot);

        Assert.Equal(
            Path.Combine(appDataRoot, LauncherApplicationIdentity.StorageDirectoryName, "accounts"),
            pathProvider.DefaultAccountDataDirectory);
    }
}
