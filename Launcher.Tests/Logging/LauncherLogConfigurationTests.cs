using Launcher.App.Logging;
using Serilog.Events;

namespace Launcher.Tests.Logging;

public sealed class LauncherLogConfigurationTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "launcher-log-cleanup-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DiagnosticLoggingControllerSwitchesBetweenNormalAndVerboseLevels()
    {
        var controller = new LauncherLogLevelController(enableDiagnosticLogging: false);

        Assert.False(controller.IsDiagnosticLoggingEnabled);
        Assert.Equal(LogEventLevel.Information, controller.LevelSwitch.MinimumLevel);
        Assert.Equal(LogEventLevel.Warning, controller.MicrosoftLevelSwitch.MinimumLevel);

        controller.SetDiagnosticLoggingEnabled(true);

        Assert.True(controller.IsDiagnosticLoggingEnabled);
        Assert.Equal(LogEventLevel.Verbose, controller.LevelSwitch.MinimumLevel);
        Assert.Equal(LogEventLevel.Verbose, controller.MicrosoftLevelSwitch.MinimumLevel);

        controller.SetDiagnosticLoggingEnabled(false);

        Assert.False(controller.IsDiagnosticLoggingEnabled);
        Assert.Equal(LogEventLevel.Information, controller.LevelSwitch.MinimumLevel);
        Assert.Equal(LogEventLevel.Warning, controller.MicrosoftLevelSwitch.MinimumLevel);
    }

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

    [Fact]
    public void LogFileNameIdentifiesOneLauncherStartupInsteadOfOneDay()
    {
        var startedAt = new DateTimeOffset(2026, 7, 19, 8, 9, 10, 321, TimeSpan.FromHours(10));

        var first = LauncherLogConfiguration.CreateLogFileName(startedAt, processId: 42);
        var restarted = LauncherLogConfiguration.CreateLogFileName(startedAt.AddMilliseconds(1), processId: 43);

        Assert.Equal("bhl-20260719-080910-321-p42.log", first);
        Assert.Equal("bhl-20260719-080910-322-p43.log", restarted);
        Assert.NotEqual(first, restarted);
    }

    [Fact]
    public void PruneOldLogFilesKeepsOnlyTwentyNewestLauncherLogs()
    {
        Directory.CreateDirectory(tempRoot);
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var launcherLogs = Enumerable.Range(0, 23)
            .Select(index => WriteLog(
                $"bhl-20260719-1200{index:00}-000-p{index}.log",
                now.AddMinutes(-index)))
            .ToArray();
        var updaterLog = WriteLog("updater-20260719-120000-000.log", now);

        LauncherLogConfiguration.PruneOldLogFiles(tempRoot, now);

        Assert.Equal(LauncherLogConfiguration.MaxRetainedLauncherLogFiles, Directory.GetFiles(tempRoot, "bhl*.log").Length);
        Assert.All(launcherLogs.Take(20), path => Assert.True(File.Exists(path)));
        Assert.All(launcherLogs.Skip(20), path => Assert.False(File.Exists(path)));
        Assert.True(File.Exists(updaterLog));
    }

    [Fact]
    public void StartupPruningCanReserveOneSlotForTheNewLog()
    {
        Directory.CreateDirectory(tempRoot);
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 20; index++)
            WriteLog($"bhl-existing-{index:00}.log", now.AddMinutes(-index));

        LauncherLogConfiguration.PruneOldLogFiles(
            tempRoot,
            now,
            LauncherLogConfiguration.MaxRetainedLauncherLogFiles - 1);

        Assert.Equal(19, Directory.GetFiles(tempRoot, "bhl*.log").Length);
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
