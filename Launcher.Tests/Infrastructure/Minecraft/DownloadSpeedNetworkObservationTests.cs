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

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }
}
