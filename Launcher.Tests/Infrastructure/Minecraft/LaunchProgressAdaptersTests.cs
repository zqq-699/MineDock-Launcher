/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchProgressAdaptersTests
{
    [Fact]
    public void LoaderRepairSequenceMapsToLaunchStagesAndMonotonicOverallRanges()
    {
        var reports = new List<LauncherProgress>();
        var carrier = DownloadSpeedTaskProgress.Create(
            reports.Add,
            reports.Add,
            out var lifetime);
        using (lifetime)
        {
            var adapter = new LaunchRepairProgressAdapter(carrier);
            Assert.Same(SpeedMeterProgress.TryGet(carrier), adapter.SpeedMeter);

            adapter.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));
            adapter.Report(new LauncherProgress(InstallProgressStages.DownloadingLoaderInstaller, string.Empty));
            adapter.Report(new LauncherProgress(InstallProgressStages.CheckingJava, string.Empty));
            adapter.Report(new LauncherProgress(InstallProgressStages.DownloadingJava, string.Empty));
            adapter.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));
            adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 10));
            adapter.Report(new LauncherProgress(LaunchProgressStages.DownloadingFiles, string.Empty, 80));
            adapter.Report(new LauncherProgress(InstallProgressStages.FinalizingVersion, string.Empty));
            adapter.Report(new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty));
            adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 50));
            adapter.ReportLoaderPublication(completed: false);
            adapter.ReportLoaderPublication(completed: true);
            adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingInstance, string.Empty, 6));
            adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingMetadata, string.Empty, 18));
            adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingJar, string.Empty, 32));
            adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingLibraries, string.Empty, 48));
            adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingAssets, string.Empty, 64));
            adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingLogging, string.Empty, 80));
            adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 90));
            adapter.Report(new LauncherProgress(
                string.Empty,
                string.Empty,
                DownloadSpeedTelemetry: new DownloadSpeedTelemetry(1024)));
        }

        var stages = reports
            .Where(report => report.DownloadSpeedTelemetry is null)
            .Select(report => report.Stage)
            .ToArray();
        Assert.DoesNotContain(stages, stage => stage.StartsWith("Install.", StringComparison.Ordinal));
        Assert.Contains(LaunchProgressStages.RepairingLoaderInstaller, stages);
        Assert.Contains(LaunchProgressStages.CheckingJava, stages);
        Assert.Contains(LaunchProgressStages.DownloadingJava, stages);
        Assert.Contains(LaunchProgressStages.RunningLoaderInstaller, stages);
        Assert.Contains(LaunchProgressStages.FinalizingLoaderVersion, stages);
        Assert.Contains(LaunchProgressStages.PublishingLoaderArtifacts, stages);
        Assert.Equal(65, reports.First(report => report.Stage == LaunchProgressStages.PublishingLoaderArtifacts).Percent);
        Assert.Equal(70, reports.Last(report => report.Stage == LaunchProgressStages.PublishingLoaderArtifacts).Percent);
        Assert.Equal(84, reports.Last(report => report.Stage == LaunchProgressStages.CheckingFiles).Percent);
        AssertMonotonic(reports);
        Assert.Contains(reports, report => report.DownloadSpeedTelemetry?.BytesPerSecond == 1024);
    }

    [Fact]
    public void StandardRepairWithoutLoaderUsesFullTwelveToEightyFourRange()
    {
        var reports = new List<LauncherProgress>();
        var adapter = new LaunchRepairProgressAdapter(new InlineProgress(reports));

        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingInstance, string.Empty, 6));
        adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingMetadata, string.Empty, 18));
        adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingJar, string.Empty, 32));
        adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingLibraries, string.Empty, 48));
        adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingAssets, string.Empty, 64));
        adapter.Report(new LauncherProgress(LaunchProgressStages.RepairingLogging, string.Empty, 80));
        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 90));

        Assert.Equal(12, reports[0].Percent);
        Assert.Equal(84, reports[^1].Percent);
        AssertMonotonic(reports);
    }

    [Fact]
    public void FinalCmlProgressIsScaledToNinetyFourThroughNinetyNineWithoutRegression()
    {
        var reports = new List<LauncherProgress>();
        var adapter = new LaunchProcessBuildProgressAdapter(new InlineProgress(reports));

        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 0));
        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 50));
        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 20));
        adapter.Report(new LauncherProgress(LaunchProgressStages.DownloadingFiles, string.Empty, 100));

        Assert.Equal([94d, 96.5d, 96.5d, 99d], reports.Select(report => report.Percent!.Value));
    }

    [Fact]
    public void JavaProvisioningProgressKeepsJavaStageAndScalesLocalPercent()
    {
        var reports = new List<LauncherProgress>();
        var adapter = new CmlLibJavaRuntimeProvisioningService.JavaRuntimeProvisioningProgress(
            new InlineProgress(reports));

        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 0));
        adapter.Report(new LauncherProgress(LaunchProgressStages.DownloadingFiles, string.Empty, 50));
        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 100));

        Assert.All(reports, report => Assert.Equal(LaunchProgressStages.CheckingJava, report.Stage));
        Assert.Equal([90d, 92d, 94d], reports.Select(report => report.Percent!.Value));
    }

    [Fact]
    public void InstallerJavaProvisioningProgressUsesDownloadingJavaStage()
    {
        var reports = new List<LauncherProgress>();
        var adapter = new CmlLibJavaRuntimeProvisioningService.InstallerJavaRuntimeProvisioningProgress(
            new InlineProgress(reports));

        adapter.Report(new LauncherProgress(LaunchProgressStages.CheckingFiles, string.Empty, 50));

        var report = Assert.Single(reports);
        Assert.Equal(InstallProgressStages.DownloadingJava, report.Stage);
        Assert.Equal(50, report.Percent);
    }

    [Fact]
    public void InstallerJavaProvisioningSpeedTelemetryKeepsDownloadingJavaStage()
    {
        var reports = new List<LauncherProgress>();
        var adapter = new CmlLibJavaRuntimeProvisioningService.InstallerJavaRuntimeProvisioningProgress(
            new InlineProgress(reports));
        var telemetry = new DownloadSpeedTelemetry(1024);

        adapter.Report(new LauncherProgress(
            LaunchProgressStages.DownloadingFiles,
            string.Empty,
            DownloadSpeedTelemetry: telemetry));

        var report = Assert.Single(reports);
        Assert.Equal(InstallProgressStages.DownloadingJava, report.Stage);
        Assert.Same(telemetry, report.DownloadSpeedTelemetry);
    }

    private static void AssertMonotonic(IEnumerable<LauncherProgress> reports)
    {
        var percents = reports
            .Where(report => report.DownloadSpeedTelemetry is null && report.Percent is not null)
            .Select(report => report.Percent!.Value)
            .ToArray();
        Assert.Equal(percents.Order(), percents);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
