using Launcher.Application;
using Launcher.Infrastructure;

namespace Launcher.Tests.Infrastructure;

public sealed class LauncherPathProviderTests : TestTempDirectory
{
    [Fact]
    public void DefaultDataDirectoryUsesFixedApplicationIdInApplicationData()
    {
        var pathProvider = new LauncherPathProvider(applicationDataDirectory: TempRoot);

        Assert.Equal(Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName), pathProvider.DefaultDataDirectory);
        Assert.Equal(LauncherApplicationIdentity.StorageDirectoryName, pathProvider.ApplicationId);
    }

    [Fact]
    public void DefaultDataDirectoryDoesNotDependOnExecutableName()
    {
        var firstProvider = new LauncherPathProvider(applicationDataDirectory: TempRoot);
        var secondProvider = new LauncherPathProvider(applicationDataDirectory: TempRoot);

        Assert.Equal(firstProvider.DefaultDataDirectory, secondProvider.DefaultDataDirectory);
        Assert.Equal(Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName), firstProvider.DefaultDataDirectory);
    }
}
