using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Application.Downloads;

public sealed class SpeedMeterTests
{
    [Fact]
    public void OneSchedulerSeparatelySamplesMultipleTasks()
    {
        var clock = new ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var firstReports = new List<LauncherProgress>();
        var secondReports = new List<LauncherProgress>();
        var first = new SpeedMeter(new InlineProgress(firstReports), scheduler);
        var second = new SpeedMeter(new InlineProgress(secondReports), scheduler);

        first.ReportBytes(10 * 1024 * 1024);
        second.ReportBytes(20 * 1024 * 1024);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();

        Assert.Equal(1, clock.CreatedTimerCount);
        Assert.Equal(2, scheduler.ActiveMeterCount);
        Assert.Equal(20 * 1024 * 1024, Assert.Single(firstReports).DownloadSpeedTelemetry!.BytesPerSecond);
        Assert.Equal(40 * 1024 * 1024, Assert.Single(secondReports).DownloadSpeedTelemetry!.BytesPerSecond);
    }

    [Fact]
    public void AggregatesConcurrentReadersWithinOneTask()
    {
        var clock = new ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var reports = new List<LauncherProgress>();
        var meter = new SpeedMeter(new InlineProgress(reports), scheduler);

        Parallel.For(0, 64, _ => meter.ReportBytes(512 * 1024));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();

        Assert.Equal(64L * 1024 * 1024, Assert.Single(reports).DownloadSpeedTelemetry!.BytesPerSecond);
    }

    [Fact]
    public void UsesActualMonotonicSamplingInterval()
    {
        var clock = new ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var reports = new List<LauncherProgress>();
        var meter = new SpeedMeter(new InlineProgress(reports), scheduler);

        meter.ReportBytes(4 * 1024 * 1024);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        scheduler.Tick();

        Assert.Equal(
            16 * 1024 * 1024,
            Assert.Single(reports).DownloadSpeedTelemetry!.BytesPerSecond);
    }

    [Fact]
    public void ContinuousFiveHundredMegabitTrafficDoesNotFallIntoKilobytes()
    {
        var clock = new ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var reports = new List<LauncherProgress>();
        var meter = new SpeedMeter(new InlineProgress(reports), scheduler);

        for (var interval = 0; interval < 4; interval++)
        {
            for (var chunk = 0; chunk < 50; chunk++)
                meter.ReportBytes(625_000);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            scheduler.Tick();
        }

        Assert.Equal(1, clock.TimerChangeCount);
        Assert.Equal(4, reports.Count);
        Assert.All(reports, report => Assert.Equal(
            62_500_000,
            report.DownloadSpeedTelemetry!.BytesPerSecond));
    }

    [Fact]
    public void IdlePauseAndStopClearWithoutAllowingLateBytesToReviveMeter()
    {
        var clock = new ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var reports = new List<LauncherProgress>();
        var meter = new SpeedMeter(new InlineProgress(reports), scheduler);

        meter.ReportBytes(1024);
        Assert.Equal(1, clock.TimerChangeCount);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();

        Assert.Equal(0, scheduler.ActiveMeterCount);
        Assert.Equal(2, clock.TimerChangeCount);
        Assert.Null(reports[^1].DownloadSpeedTelemetry!.BytesPerSecond);

        meter.ReportBytes(2048);
        Assert.Equal(3, clock.TimerChangeCount);
        meter.Pause();
        Assert.Equal(0, scheduler.ActiveMeterCount);
        Assert.Equal(4, clock.TimerChangeCount);
        meter.Resume();
        meter.ReportBytes(4096);
        Assert.Equal(5, clock.TimerChangeCount);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();
        meter.Stop();
        Assert.Equal(6, clock.TimerChangeCount);
        meter.ReportBytes(1024 * 1024);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();

        Assert.Equal(0, scheduler.ActiveMeterCount);
        Assert.Null(reports[^1].DownloadSpeedTelemetry!.BytesPerSecond);
    }

    [Fact]
    public void ProgressMappersPreserveTaskMeterIdentity()
    {
        var reports = new List<LauncherProgress>();
        var root = DownloadSpeedTaskProgress.Create(
            reports.Add,
            reports.Add,
            out var lifetime);
        using (lifetime)
        {
            var install = new InstallCardProgressMapper(root, LoaderKind.Forge, hasOptionalContent: false);
            var import = new OverallModpackImportProgress(install);

            Assert.Same(
                ((ISpeedMeterProgress)root).SpeedMeter,
                ((ISpeedMeterProgress)install).SpeedMeter);
            Assert.Same(
                ((ISpeedMeterProgress)root).SpeedMeter,
                ((ISpeedMeterProgress)import).SpeedMeter);
        }
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    internal sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        private readonly ManualTimer timer = new();

        public int CreatedTimerCount { get; private set; }
        public int TimerChangeCount => timer.ChangeCount;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan duration) => timestamp += duration.Ticks;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            CreatedTimerCount++;
            return timer;
        }

        private sealed class ManualTimer : ITimer
        {
            public int ChangeCount { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ChangeCount++;
                return true;
            }
            public void Dispose()
            {
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
