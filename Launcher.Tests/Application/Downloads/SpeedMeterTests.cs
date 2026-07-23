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
    public void InFlightReadKeepsItsOriginalMeasurementWindowAcrossEmptyTicks()
    {
        var clock = new ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var reports = new List<LauncherProgress>();
        var meter = new SpeedMeter(new InlineProgress(reports), scheduler);

        var observation = meter.BeginRead();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();

        Assert.Empty(reports);
        Assert.Equal(1, scheduler.ActiveMeterCount);

        clock.Advance(TimeSpan.FromSeconds(3.5));
        meter.CompleteRead(observation, 128 * 1024);
        scheduler.Tick();

        Assert.Equal(
            32 * 1024,
            Assert.Single(reports).DownloadSpeedTelemetry!.BytesPerSecond);
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
