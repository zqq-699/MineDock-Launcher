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
    private static readonly TimeSpan ClearDelay = TimeSpan.FromMilliseconds(750);
    private readonly object syncRoot = new();
    private readonly IProgress<LauncherProgress>? progress;
    private readonly SlidingWindowDownloadSpeedMeter meter;
    private readonly Timer timer;
    private readonly string speedStage;
    private readonly string inactiveStage;
    private readonly Func<string?>? messageProvider;
    private readonly Func<DateTimeOffset> clock;
    private int activeTransfers;
    private string? lastReportedSpeed;
    private DateTimeOffset? lastTransferEndedAt;
    private bool disposed;

    public SlidingWindowDownloadSpeedReporter(
        IProgress<LauncherProgress>? progress,
        SlidingWindowDownloadSpeedMeter? meter = null,
        string speedStage = LaunchProgressStages.DownloadSpeed,
        string inactiveStage = LaunchProgressStages.CheckingFiles,
        Func<string?>? messageProvider = null,
        Func<DateTimeOffset>? clock = null)
    {
        this.progress = progress;
        this.meter = meter ?? new SlidingWindowDownloadSpeedMeter();
        this.speedStage = speedStage;
        this.inactiveStage = inactiveStage;
        this.messageProvider = messageProvider;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        timer = new Timer(static state => ((SlidingWindowDownloadSpeedReporter)state!).Refresh(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal bool HasActiveTransfers
    {
        get
        {
            lock (syncRoot)
                return !disposed && activeTransfers > 0;
        }
    }

    public void BeginTransfer()
    {
        lock (syncRoot)
        {
            if (disposed)
                return;
            activeTransfers++;
            lastTransferEndedAt = null;
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
        lock (syncRoot)
        {
            if (disposed || activeTransfers <= 0)
                return;
            activeTransfers--;
            if (activeTransfers == 0)
            {
                // Do not discard the recent body-read samples here. A batch often
                // advances from one small file to the next before two seconds pass;
                // those adjacent network reads form one valid sliding window.
                lastTransferEndedAt = clock();
            }
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
                return;
            // A reporter is normally disposed by its operation scope immediately
            // after the last file. Keep its timer alive through the short grace
            // period so a completed short download can still publish and then
            // explicitly clear its last speed.
            if (activeTransfers == 0 && lastTransferEndedAt is null)
            {
                disposed = true;
                timer.Dispose();
                meter.Clear();
            }
        }
    }

    internal void Refresh()
    {
        string? speed = null;
        var shouldClear = false;
        lock (syncRoot)
        {
            if (disposed)
                return;
            if (activeTransfers == 0 && lastTransferEndedAt is { } endedAt
                && clock() - endedAt >= ClearDelay)
            {
                shouldClear = lastReportedSpeed is not null;
                lastReportedSpeed = null;
                lastTransferEndedAt = null;
                meter.Clear();
                disposed = true;
                timer.Dispose();
            }
            else
            {
                // Once a completed transfer has published its final value, hold
                // that value through the grace period instead of decaying it
                // while no response body is being read.
                if (activeTransfers == 0 && lastReportedSpeed is not null)
                    return;
                speed = meter.GetSpeedText();
                if (speed is null || string.Equals(speed, lastReportedSpeed, StringComparison.Ordinal))
                    return;
                lastReportedSpeed = speed;
                shouldClear = false;
            }
        }
        ReportSpeed(shouldClear ? string.Empty : speed!);
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
    private DateTimeOffset? samplingStartedAt;

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
            Trim(now);
            if (samples.Count == 0)
                samplingStartedAt = now;
            samples.Enqueue(new Sample(now, bytesDelta));
        }
    }

    public string? GetSpeedText()
    {
        lock (syncRoot)
        {
            var now = clock();
            Trim(now);
            if (samples.Count == 0)
            {
                samplingStartedAt = null;
                return null;
            }

            // Trimming removes the oldest sample as soon as the clock moves past
            // the window boundary. Keep the sampling start separately so a live
            // transfer remains measurable after that boundary has passed.
            if (samplingStartedAt is not { } startedAt)
                return null;

            var elapsed = now - startedAt;
            var denominator = elapsed < Window ? elapsed : Window;
            if (denominator < TimeSpan.FromMilliseconds(500))
                return null;

            var bytesPerSecond = samples.Sum(sample => sample.Bytes) / denominator.TotalSeconds;
            return FormatSpeed(bytesPerSecond);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            samples.Clear();
            samplingStartedAt = null;
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
