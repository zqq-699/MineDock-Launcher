/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Reports only bytes received from a response body.  The fixed two-second
/// window deliberately excludes address resolution, throttling waits, hashing
/// and local file operations.
/// </summary>
internal sealed class SlidingWindowDownloadSpeedReporter : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);
    private readonly object syncRoot = new();
    private readonly IProgress<LauncherProgress>? progress;
    private readonly SlidingWindowDownloadSpeedMeter meter;
    private readonly Timer timer;
    private readonly string speedStage;
    private readonly string inactiveStage;
    private readonly Func<string?>? messageProvider;
    private int activeTransfers;
    private string? lastReportedSpeed;
    private bool disposed;

    public SlidingWindowDownloadSpeedReporter(
        IProgress<LauncherProgress>? progress,
        SlidingWindowDownloadSpeedMeter? meter = null,
        string speedStage = LaunchProgressStages.DownloadSpeed,
        string inactiveStage = LaunchProgressStages.CheckingFiles,
        Func<string?>? messageProvider = null)
    {
        this.progress = progress;
        this.meter = meter ?? new SlidingWindowDownloadSpeedMeter();
        this.speedStage = speedStage;
        this.inactiveStage = inactiveStage;
        this.messageProvider = messageProvider;
        timer = new Timer(static state => ((SlidingWindowDownloadSpeedReporter)state!).Refresh(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void BeginTransfer()
    {
        lock (syncRoot)
        {
            if (disposed)
                return;
            activeTransfers++;
            timer.Change(RefreshInterval, RefreshInterval);
        }
    }

    public void ReportNetworkBytes(long bytesDelta)
    {
        if (bytesDelta <= 0)
            return;
        lock (syncRoot)
        {
            if (!disposed)
                meter.RecordNetworkBytes(bytesDelta);
        }
    }

    public void EndTransfer()
    {
        var shouldClear = false;
        lock (syncRoot)
        {
            if (disposed || activeTransfers <= 0)
                return;
            activeTransfers--;
            if (activeTransfers == 0)
            {
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                // Do not discard the recent body-read samples here. A batch often
                // advances from one small file to the next before two seconds pass;
                // those adjacent network reads form one valid sliding window.
                shouldClear = lastReportedSpeed is not null;
                lastReportedSpeed = null;
            }
        }
        if (shouldClear)
            progress?.Report(new LauncherProgress(
                inactiveStage,
                GetMessage(),
                DownloadSpeedText: string.Empty));
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
                return;
            disposed = true;
            timer.Dispose();
            meter.Clear();
            activeTransfers = 0;
        }
    }

    internal void Refresh()
    {
        string? speed;
        lock (syncRoot)
        {
            if (disposed || activeTransfers == 0)
                return;
            speed = meter.GetSpeedText();
            if (string.Equals(speed, lastReportedSpeed, StringComparison.Ordinal))
                return;
            lastReportedSpeed = speed;
        }
        ReportSpeed(speed ?? string.Empty);
    }

    private void ReportSpeed(string speed) => progress?.Report(new LauncherProgress(
        speedStage,
        GetMessage(),
        DownloadSpeedText: speed));

    private string GetMessage() => messageProvider?.Invoke() ?? string.Empty;
}

/// <summary>Per-file activity bridge so parallel downloads share one aggregate meter safely.</summary>
internal sealed class DownloadActivitySpeedSession : IDisposable
{
    private readonly SlidingWindowDownloadSpeedReporter reporter;
    private bool transferActive;

    public DownloadActivitySpeedSession(SlidingWindowDownloadSpeedReporter reporter)
    {
        this.reporter = reporter;
    }

    public void Report(DownloadFileActivity activity)
    {
        if (activity is DownloadFileActivity.Downloading && !transferActive)
        {
            transferActive = true;
            reporter.BeginTransfer();
        }
        else if (activity is not DownloadFileActivity.Downloading && transferActive)
        {
            transferActive = false;
            reporter.EndTransfer();
        }
    }

    public void Dispose() => Report(DownloadFileActivity.Verifying);
}

internal sealed class SlidingWindowDownloadSpeedMeter
{
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private readonly object syncRoot = new();
    private readonly Queue<Sample> samples = new();
    private readonly Func<DateTimeOffset> clock;

    public SlidingWindowDownloadSpeedMeter(Func<DateTimeOffset>? clock = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void RecordNetworkBytes(long bytesDelta)
    {
        if (bytesDelta <= 0)
            return;
        lock (syncRoot)
        {
            var now = clock();
            samples.Enqueue(new Sample(now, bytesDelta));
            Trim(now);
        }
    }

    public string? GetSpeedText()
    {
        lock (syncRoot)
        {
            var now = clock();
            Trim(now);
            if (samples.Count == 0 || now - samples.Peek().Timestamp < Window)
                return null;
            var bytesPerSecond = samples.Sum(sample => sample.Bytes) / Window.TotalSeconds;
            return FormatSpeed(bytesPerSecond);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            samples.Clear();
        }
    }

    private void Trim(DateTimeOffset now)
    {
        var cutoff = now - Window;
        while (samples.TryPeek(out var sample) && sample.Timestamp < cutoff)
            samples.Dequeue();
    }

    private static string FormatSpeed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0.0} KB/s",
        _ => $"{bytesPerSecond:0} B/s"
    };

    private readonly record struct Sample(DateTimeOffset Timestamp, long Bytes);
}
