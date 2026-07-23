using System.Net;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Application.Downloads;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class DownloadSpeedNetworkObservationTests
{
    [Fact]
    public async Task CountsEachSuccessfulNetworkReadExactlyOnce()
    {
        var clock = new SpeedMeterTests.ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var reports = new List<LauncherProgress>();
        var meter = new SpeedMeter(new InlineProgress(reports), scheduler);
        var payload = new byte[4 * 1024 * 1024];
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(payload, writable: false))
        };

        await DownloadResponseThrottler.ApplyAsync(
            response,
            bandwidthLimiter: null,
            CancellationToken.None,
            speedMeter: meter);
        await using var destination = new MemoryStream();
        await response.Content.CopyToAsync(destination, CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        scheduler.Tick();

        Assert.Equal(payload.Length, destination.Length);
        Assert.Equal(
            8 * 1024 * 1024,
            Assert.Single(reports).DownloadSpeedTelemetry!.BytesPerSecond);
    }

    [Fact]
    public async Task IncludesSlowReadDurationInDisplayedSpeed()
    {
        var clock = new SpeedMeterTests.ManualTimeProvider();
        using var scheduler = new SpeedMeterScheduler(clock);
        var reports = new List<LauncherProgress>();
        var meter = new SpeedMeter(new InlineProgress(reports), scheduler);
        var payload = new byte[128 * 1024];
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new TimedReadStream(
                payload,
                clock,
                TimeSpan.FromSeconds(4)))
        };

        await DownloadResponseThrottler.ApplyAsync(
            response,
            bandwidthLimiter: null,
            CancellationToken.None,
            speedMeter: meter);
        await using var destination = new MemoryStream();
        await response.Content.CopyToAsync(destination, CancellationToken.None);
        scheduler.Tick();

        Assert.Equal(payload.Length, destination.Length);
        Assert.Equal(
            32 * 1024,
            Assert.Single(reports).DownloadSpeedTelemetry!.BytesPerSecond);
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    private sealed class TimedReadStream(
        byte[] payload,
        SpeedMeterTests.ManualTimeProvider clock,
        TimeSpan readDuration) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => payload.Length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= payload.Length)
                return ValueTask.FromResult(0);

            if (position == 0)
                clock.Advance(readDuration);
            var count = Math.Min(buffer.Length, payload.Length - position);
            payload.AsSpan(position, count).CopyTo(buffer.Span);
            position += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
