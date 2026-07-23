/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

/// <summary>
/// Creates task-scoped progress carriers while keeping the meter and scheduler
/// implementation out of public download service contracts.
/// </summary>
public static class DownloadSpeedTaskProgress
{
    public static IProgress<LauncherProgress> Create(
        Action<LauncherProgress> report,
        Action<LauncherProgress> reportTelemetry,
        out IDisposable lifetime)
    {
        var meter = new SpeedMeter(new DelegateProgress(reportTelemetry));
        lifetime = new MeterLifetime(meter);
        return new SpeedMeterProgress(meter, report);
    }

    public static IProgress<LauncherProgress> Forward(
        IProgress<LauncherProgress> parent,
        Action<LauncherProgress> report) => SpeedMeterProgress.Forward(parent, report);

    public static IProgress<T> Carry<T>(
        IProgress<LauncherProgress> parent,
        IProgress<T> progress) => SpeedMeterProgress.Carry(parent, progress);

    private sealed class DelegateProgress(Action<LauncherProgress> report) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => report(value);
    }

    private sealed class MeterLifetime(SpeedMeter meter) : IDisposable
    {
        private SpeedMeter? meter = meter;
        public void Dispose() => Interlocked.Exchange(ref meter, null)?.Stop();
    }
}

/// <summary>
/// Carries one user-visible task's meter through the existing progress contract
/// without adding telemetry parameters to public download interfaces.
/// </summary>
internal interface ISpeedMeterProgress
{
    SpeedMeter? SpeedMeter { get; }
}

internal sealed class SpeedMeterProgress : IProgress<LauncherProgress>, ISpeedMeterProgress
{
    private readonly Action<LauncherProgress> report;

    public SpeedMeterProgress(SpeedMeter speedMeter, Action<LauncherProgress> report)
    {
        SpeedMeter = speedMeter;
        this.report = report;
    }

    public SpeedMeter SpeedMeter { get; }

    public void Report(LauncherProgress value) => report(value);

    public static IProgress<LauncherProgress> Forward(
        IProgress<LauncherProgress> innerProgress,
        Action<LauncherProgress> report)
    {
        var speedMeter = TryGet(innerProgress);
        return speedMeter is null
            ? new DelegateProgress(report)
            : new SpeedMeterProgress(speedMeter, report);
    }

    public static IProgress<T> Carry<T>(
        IProgress<LauncherProgress> innerProgress,
        IProgress<T> progress)
    {
        var speedMeter = TryGet(innerProgress);
        return speedMeter is null
            ? progress
            : new CarriedProgress<T>(speedMeter, progress);
    }

    public static SpeedMeter? TryGet(object? progress) =>
        (progress as ISpeedMeterProgress)?.SpeedMeter;

    private sealed class DelegateProgress(Action<LauncherProgress> report) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => report(value);
    }

    private sealed class CarriedProgress<T>(SpeedMeter speedMeter, IProgress<T> progress)
        : IProgress<T>, ISpeedMeterProgress
    {
        public SpeedMeter SpeedMeter { get; } = speedMeter;

        public void Report(T value) => progress.Report(value);
    }
}

/// <summary>
/// Thread-safe byte accumulator for exactly one user-visible operation.
/// All instances are sampled by <see cref="SpeedMeterScheduler.Shared"/>.
/// </summary>
internal sealed class SpeedMeter
{
    private const int Idle = 0;
    private const int Active = 1;
    private const int Sampling = 2;
    private const int Paused = 3;
    private const int Stopped = 4;

    private readonly object lifecycleLock = new();
    private readonly IProgress<LauncherProgress> telemetryProgress;
    private readonly SpeedMeterScheduler scheduler;
    private long pendingBytes;
    private long sampleStartedAt;
    private long readGeneration;
    private int activeReads;
    private int state;
    private bool isVisible;

    public SpeedMeter(
        IProgress<LauncherProgress> telemetryProgress,
        SpeedMeterScheduler? scheduler = null)
    {
        this.telemetryProgress = telemetryProgress;
        this.scheduler = scheduler ?? SpeedMeterScheduler.Shared;
    }

    public void ReportBytes(long bytes)
    {
        if (bytes <= 0)
            return;

        var observation = BeginRead();
        CompleteRead(observation, bytes);
    }

    internal long BeginRead()
    {
        lock (lifecycleLock)
        {
            if (state is Paused or Stopped)
                return -1;

            activeReads++;
            if (state == Idle)
            {
                state = Active;
                sampleStartedAt = scheduler.GetTimestamp();
                scheduler.Activate(this);
            }
            return readGeneration;
        }
    }

    internal void CompleteRead(long observation, long bytes)
    {
        if (observation < 0)
            return;

        lock (lifecycleLock)
        {
            if (observation != readGeneration)
                return;

            activeReads = Math.Max(0, activeReads - 1);
            if (state is Paused or Stopped || bytes <= 0)
                return;

            pendingBytes += bytes;
        }
    }

    public void Pause()
    {
        lock (lifecycleLock)
        {
            var previous = state;
            state = Paused;
            readGeneration++;
            activeReads = 0;
            pendingBytes = 0;
            if (previous is Active or Sampling)
                scheduler.Deactivate(this);
            ClearVisibleValue();
        }
    }

    public void Resume()
    {
        lock (lifecycleLock)
        {
            if (state != Paused)
                return;
            pendingBytes = 0;
            state = Idle;
        }
    }

    public void Stop()
    {
        lock (lifecycleLock)
        {
            var previous = state;
            if (previous == Stopped)
                return;
            state = Stopped;
            readGeneration++;
            activeReads = 0;
            pendingBytes = 0;
            if (previous is Active or Sampling)
                scheduler.Deactivate(this);
            ClearVisibleValue(force: true);
        }
    }

    internal void Sample(long now)
    {
        lock (lifecycleLock)
        {
            if (state != Active)
                return;
            state = Sampling;

            var bytes = pendingBytes;
            pendingBytes = 0;
            var startedAt = sampleStartedAt;
            if (bytes <= 0)
            {
                if (activeReads > 0)
                {
                    state = Active;
                    ClearVisibleValue();
                    return;
                }

                state = Idle;
                sampleStartedAt = now;
                scheduler.Deactivate(this);
                ClearVisibleValue();
                return;
            }

            var elapsed = scheduler.GetElapsedTime(startedAt, now);
            if (elapsed <= TimeSpan.Zero)
            {
                pendingBytes += bytes;
                state = Active;
                return;
            }

            sampleStartedAt = now;
            state = Active;
            var bytesPerSecond = (long)Math.Round(
                bytes / elapsed.TotalSeconds,
                MidpointRounding.AwayFromZero);
            isVisible = true;
            Publish(new DownloadSpeedTelemetry(bytesPerSecond));
        }
    }

    private void ClearVisibleValue(bool force = false)
    {
        if (!isVisible && !force)
            return;
        isVisible = false;
        Publish(DownloadSpeedTelemetry.Clear);
    }

    private void Publish(DownloadSpeedTelemetry telemetry) => telemetryProgress.Report(
        new LauncherProgress(string.Empty, string.Empty, DownloadSpeedTelemetry: telemetry));
}

/// <summary>
/// Owns the process-wide 500 ms sampling timer. The timer runs only while at
/// least one task currently has network bytes to sample.
/// </summary>
internal sealed class SpeedMeterScheduler : IDisposable
{
    internal static readonly TimeSpan SamplingInterval = TimeSpan.FromMilliseconds(500);
    public static SpeedMeterScheduler Shared { get; } = new(TimeProvider.System);

    private readonly object syncRoot = new();
    private readonly HashSet<SpeedMeter> activeMeters = [];
    private readonly TimeProvider timeProvider;
    private readonly ITimer timer;
    private int isTicking;
    private bool disposed;

    internal SpeedMeterScheduler(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        timer = timeProvider.CreateTimer(
            static state => ((SpeedMeterScheduler)state!).Tick(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    internal int ActiveMeterCount
    {
        get
        {
            lock (syncRoot)
                return activeMeters.Count;
        }
    }

    internal long GetTimestamp() => timeProvider.GetTimestamp();

    internal TimeSpan GetElapsedTime(long start, long end) => timeProvider.GetElapsedTime(start, end);

    internal void Activate(SpeedMeter meter)
    {
        lock (syncRoot)
        {
            if (disposed || !activeMeters.Add(meter) || activeMeters.Count != 1)
                return;
            timer.Change(SamplingInterval, SamplingInterval);
        }
    }

    internal void Deactivate(SpeedMeter meter)
    {
        lock (syncRoot)
        {
            if (!activeMeters.Remove(meter) || activeMeters.Count != 0)
                return;
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    internal void Tick()
    {
        if (Interlocked.Exchange(ref isTicking, 1) != 0)
            return;

        try
        {
            SpeedMeter[] snapshot;
            lock (syncRoot)
            {
                if (disposed)
                    return;
                snapshot = activeMeters.ToArray();
            }

            var now = GetTimestamp();
            foreach (var meter in snapshot)
                meter.Sample(now);
        }
        finally
        {
            Volatile.Write(ref isTicking, 0);
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
                return;
            disposed = true;
            activeMeters.Clear();
            timer.Dispose();
        }
    }
}
