/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchSessionDiagnosticCollectorTests : TestTempDirectory
{
    [Fact]
    public async Task CollectOrdersOnlyFilesCreatedOrUpdatedByCurrentSession()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Example");
        var crashDirectory = Path.Combine(instanceDirectory, "crash-reports");
        var logsDirectory = Path.Combine(instanceDirectory, "logs");
        Directory.CreateDirectory(crashDirectory);
        Directory.CreateDirectory(logsDirectory);

        var staleCrash = Path.Combine(crashDirectory, "crash-stale-client.txt");
        var latestLog = Path.Combine(logsDirectory, "latest.log");
        await File.WriteAllTextAsync(staleCrash, "old crash");
        await File.WriteAllTextAsync(latestLog, "old log");

        var collector = new LaunchSessionDiagnosticCollector(
            minecraftDirectory,
            instanceDirectory,
            maximumWait: TimeSpan.FromMilliseconds(100),
            pollInterval: TimeSpan.FromMilliseconds(5),
            stableDuration: TimeSpan.FromMilliseconds(15));
        var processStartedAt = DateTimeOffset.UtcNow;

        var newCrash = Path.Combine(crashDirectory, "crash-current-client.txt");
        var jvmCrash = Path.Combine(instanceDirectory, "hs_err_pid123.log");
        var capturedOutput = Path.Combine(
            instanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "logs",
            "launch-output-test.log");
        Directory.CreateDirectory(Path.GetDirectoryName(capturedOutput)!);
        await File.WriteAllTextAsync(newCrash, "current crash");
        await File.WriteAllTextAsync(jvmCrash, "jvm crash");
        await File.WriteAllTextAsync(latestLog, "updated current session log with a different length");
        await File.WriteAllTextAsync(capturedOutput, "[stderr] current output");

        var candidates = await collector.CollectAsync(processStartedAt, capturedOutput, CancellationToken.None);

        Assert.Equal(
            [
                LaunchDiagnosticType.MinecraftCrashReport,
                LaunchDiagnosticType.JvmCrashReport,
                LaunchDiagnosticType.MinecraftLatestLog,
                LaunchDiagnosticType.CapturedOutput
            ],
            candidates.Select(candidate => candidate.Type));
        Assert.DoesNotContain(candidates, candidate => candidate.Path == staleCrash);
        Assert.Equal(newCrash, candidates[0].Path);
        Assert.Equal(jvmCrash, candidates[1].Path);
        Assert.Equal(latestLog, candidates[2].Path);
        Assert.Equal(capturedOutput, candidates[3].Path);
    }

    [Fact]
    public async Task CollectDoesNotUseRecentlyModifiedButUnchangedHistoricalFile()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", "Example");
        var crashDirectory = Path.Combine(instanceDirectory, "crash-reports");
        Directory.CreateDirectory(crashDirectory);
        var historicalCrash = Path.Combine(crashDirectory, "crash-recent-client.txt");
        await File.WriteAllTextAsync(historicalCrash, "historical crash");

        var collector = new LaunchSessionDiagnosticCollector(
            minecraftDirectory,
            instanceDirectory,
            maximumWait: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(5),
            stableDuration: TimeSpan.FromMilliseconds(10));

        var candidates = await collector.CollectAsync(DateTimeOffset.UtcNow, null, CancellationToken.None);

        Assert.Empty(candidates);
    }

}
