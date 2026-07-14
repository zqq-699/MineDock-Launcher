/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class SlidingWindowDownloadSpeedMeterTests
{
    [Fact]
    public void DisplaysAfterFiveHundredMillisecondsUsingElapsedSamplingTime()
    {
        var now = DateTimeOffset.UnixEpoch;
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);

        meter.RecordNetworkBytes(2 * 1024);
        now += TimeSpan.FromMilliseconds(500);
        Assert.Equal("4.0 KB/s", meter.GetSpeedText());
    }

    [Fact]
    public void ReportsContinuousTrafficAfterPassingWindowBoundary()
    {
        var now = DateTimeOffset.UnixEpoch;
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);

        meter.RecordNetworkBytes(2 * 1024);
        now += TimeSpan.FromSeconds(1);
        meter.RecordNetworkBytes(2 * 1024);
        now += TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(1);
        meter.RecordNetworkBytes(2 * 1024);

        Assert.Equal("2.0 KB/s", meter.GetSpeedText());
    }

    [Fact]
    public void DropsSpeedWhenNoRecentNetworkBytesRemain()
    {
        var now = DateTimeOffset.UnixEpoch;
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);
        meter.RecordNetworkBytes(4 * 1024);

        now += TimeSpan.FromSeconds(2);
        Assert.Equal("2.0 KB/s", meter.GetSpeedText());

        now += TimeSpan.FromMilliseconds(1);
        Assert.Null(meter.GetSpeedText());
    }

    [Fact]
    public void RestartsWarmupAfterTrafficBecomesInactive()
    {
        var now = DateTimeOffset.UnixEpoch;
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);
        meter.RecordNetworkBytes(4 * 1024);

        now += SlidingWindowDownloadSpeedMeter.Window + TimeSpan.FromMilliseconds(1);
        Assert.Null(meter.GetSpeedText());

        meter.RecordNetworkBytes(4 * 1024);
        now += TimeSpan.FromSeconds(1);

        Assert.Equal("4.0 KB/s", meter.GetSpeedText());
    }

    [Fact]
    public void AggregatesConcurrentNetworkByteReports()
    {
        var now = DateTimeOffset.UnixEpoch;
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);

        Parallel.For(0, 100, _ => meter.RecordNetworkBytes(1024));
        now += TimeSpan.FromSeconds(2);

        Assert.Equal("50.0 KB/s", meter.GetSpeedText());
    }

    [Fact]
    public void AdjacentSmallTransfersShareOneRecentNetworkWindow()
    {
        var now = DateTimeOffset.UnixEpoch;
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);
        var reports = new List<LauncherProgress>();
        using var reporter = new SlidingWindowDownloadSpeedReporter(
            new InlineProgress(reports),
            meter);

        reporter.BeginTransfer();
        reporter.ReportNetworkBytes(2 * 1024);
        now += TimeSpan.FromSeconds(1);
        reporter.ReportNetworkBytes(2 * 1024);
        reporter.EndTransfer();

        reporter.BeginTransfer();
        now += TimeSpan.FromSeconds(1);
        reporter.ReportNetworkBytes(2 * 1024);
        reporter.Refresh();

        Assert.Equal("3.0 KB/s", Assert.Single(reports).DownloadSpeedText);
    }

    [Fact]
    public void ShortTransferRetainsSpeedUntilGracePeriodThenExplicitlyClears()
    {
        var now = DateTimeOffset.UnixEpoch;
        var reports = new List<LauncherProgress>();
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);
        using var reporter = new SlidingWindowDownloadSpeedReporter(
            new InlineProgress(reports), meter, clock: () => now);

        reporter.BeginTransfer();
        reporter.ReportNetworkBytes(1024);
        reporter.EndTransfer();

        now += TimeSpan.FromMilliseconds(500);
        reporter.Refresh();
        now += TimeSpan.FromMilliseconds(249);
        reporter.Refresh();
        now += TimeSpan.FromMilliseconds(1);
        reporter.Refresh();

        Assert.Collection(reports,
            progress => Assert.Equal("2.0 KB/s", progress.DownloadSpeedText),
            progress => Assert.Equal(string.Empty, progress.DownloadSpeedText));
    }

    [Fact]
    public void DoesNotRepeatAnUnchangedFormattedSpeed()
    {
        var now = DateTimeOffset.UnixEpoch;
        var reports = new List<LauncherProgress>();
        var meter = new SlidingWindowDownloadSpeedMeter(() => now);
        using var reporter = new SlidingWindowDownloadSpeedReporter(
            new InlineProgress(reports), meter, clock: () => now);

        reporter.BeginTransfer();
        reporter.ReportNetworkBytes(1024);
        now += TimeSpan.FromMilliseconds(500);
        reporter.Refresh();
        reporter.Refresh();

        Assert.Single(reports);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
