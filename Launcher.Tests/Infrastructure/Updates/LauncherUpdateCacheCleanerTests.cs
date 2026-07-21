using Launcher.Application;
using Launcher.Infrastructure.Updates;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class LauncherUpdateCacheCleanerTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "launcher-update-cache-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CleanupStaleCacheDeletesAllHistoricalVersions()
    {
        WriteCachedUpdater("1.0.0");
        WriteCachedUpdater("1.0.1");
        var cleaner = CreateCleaner();

        cleaner.CleanupStaleCache(currentExecutablePath: null);

        Assert.False(Directory.Exists(GetUpdatesRoot()));
    }

    [Fact]
    public void CleanupStaleCacheProtectsUpdaterReferencedByPendingTransaction()
    {
        var protectedUpdater = WriteCachedUpdater("1.0.1");
        var staleUpdater = WriteCachedUpdater("1.0.0");
        var targetPath = Path.Combine(tempRoot, "BlockHelm-Launcher.exe");
        File.WriteAllText(targetPath, "old");
        var transaction = LauncherUpdateTransaction.Create(new LauncherUpdateApplyOptions(
            ProcessId: 0,
            SourcePath: protectedUpdater,
            TargetPath: targetPath,
            LogDirectory: Path.Combine(tempRoot, "log"),
            Restart: true));
        new LauncherUpdateFileOperations().WriteTransaction(transaction);

        CreateCleaner().CleanupStaleCache(targetPath);

        Assert.True(File.Exists(protectedUpdater));
        Assert.False(File.Exists(staleUpdater));
    }

    private LauncherUpdateCacheCleaner CreateCleaner() => new(tempRoot);

    private string WriteCachedUpdater(string version)
    {
        var directory = Path.Combine(GetUpdatesRoot(), version);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "BlockHelm-Launcher.exe");
        File.WriteAllText(path, version);
        return path;
    }

    private string GetUpdatesRoot() => Path.Combine(
        tempRoot,
        LauncherApplicationIdentity.StorageDirectoryName,
        "cache",
        "updates");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}
