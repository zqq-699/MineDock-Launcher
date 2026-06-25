using Launcher.Infrastructure;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ModpackWorkspaceCleanupServiceTests : TestTempDirectory
{
    [Fact]
    public async Task CleanupAllAsyncDeletesModpackWorkspaceDirectories()
    {
        var cacheDirectory = CreateModpackCacheDirectory();
        var firstWorkspace = Path.Combine(cacheDirectory, "first");
        var secondWorkspace = Path.Combine(cacheDirectory, "second");
        Directory.CreateDirectory(Path.Combine(firstWorkspace, "downloads"));
        Directory.CreateDirectory(secondWorkspace);
        await File.WriteAllTextAsync(Path.Combine(firstWorkspace, "downloads", "mod.jar"), "stub");
        await File.WriteAllTextAsync(Path.Combine(secondWorkspace, "manifest.json"), "stub");
        var service = CreateService();

        await service.CleanupAllAsync();

        Assert.False(Directory.Exists(firstWorkspace));
        Assert.False(Directory.Exists(secondWorkspace));
        Assert.True(Directory.Exists(cacheDirectory));
    }

    [Fact]
    public async Task CleanupAllAsyncDoesNothingWhenCacheDirectoryDoesNotExist()
    {
        var service = CreateService();

        await service.CleanupAllAsync();

        Assert.False(Directory.Exists(Path.Combine(TempRoot, "Launcher", "cache", "modpacks")));
    }

    [Fact]
    public async Task CleanupAllAsyncIgnoresFilesInModpackCacheDirectory()
    {
        var cacheDirectory = CreateModpackCacheDirectory();
        var markerFile = Path.Combine(cacheDirectory, "marker.tmp");
        await File.WriteAllTextAsync(markerFile, "keep");
        var service = CreateService();

        await service.CleanupAllAsync();

        Assert.True(File.Exists(markerFile));
    }

    private ModpackWorkspaceCleanupService CreateService()
    {
        return new ModpackWorkspaceCleanupService(new LauncherPathProvider(TempRoot));
    }

    private string CreateModpackCacheDirectory()
    {
        var cacheDirectory = Path.Combine(TempRoot, "Launcher", "cache", "modpacks");
        Directory.CreateDirectory(cacheDirectory);
        return cacheDirectory;
    }
}
