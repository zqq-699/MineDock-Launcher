using Launcher.App.Logging;

namespace Launcher.Tests.Logging;

public sealed class LauncherLogConfigurationTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "launcher-log-cleanup-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void PruneOldLogFilesDeletesExpiredLauncherAndUpdaterLogsOnly()
    {
        Directory.CreateDirectory(tempRoot);
        var now = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var expiredLauncherLog = WriteLog("bhl-20260501.log", now.AddDays(-31));
        var expiredUpdaterLog = WriteLog("updater-20260501-120000-000.log", now.AddDays(-31));
        var recentLauncherLog = WriteLog("bhl-20260712.log", now.AddDays(-1));
        var recentUpdaterLog = WriteLog("updater-20260712-120000-000.log", now.AddDays(-1));
        var unrelatedLog = WriteLog("third-party.log", now.AddDays(-90));

        LauncherLogConfiguration.PruneOldLogFiles(tempRoot, now);

        Assert.False(File.Exists(expiredLauncherLog));
        Assert.False(File.Exists(expiredUpdaterLog));
        Assert.True(File.Exists(recentLauncherLog));
        Assert.True(File.Exists(recentUpdaterLog));
        Assert.True(File.Exists(unrelatedLog));
    }

    private string WriteLog(string name, DateTimeOffset lastWriteTime)
    {
        var path = Path.Combine(tempRoot, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, lastWriteTime.UtcDateTime);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}
