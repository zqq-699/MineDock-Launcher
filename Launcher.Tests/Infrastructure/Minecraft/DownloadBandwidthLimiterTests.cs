using System.Diagnostics;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class DownloadBandwidthLimiterTests
{
    [Fact]
    public async Task SharedLimiterThrottlesConcurrentReservationsAsOneBudget()
    {
        var limiter = DownloadBandwidthLimiter.Create(1);
        Assert.NotNull(limiter);

        var payloadBytes = 1024 * 1024;
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(
            limiter!.ThrottleAsync(payloadBytes, CancellationToken.None).AsTask(),
            limiter.ThrottleAsync(payloadBytes, CancellationToken.None).AsTask());

        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(850));
    }

    [Fact]
    public async Task LimiterUsesLatestSharedSpeedLimitForSubsequentReads()
    {
        var speedLimitState = new TestDownloadSpeedLimitState();
        var limiter = DownloadBandwidthLimiter.Create(0, speedLimitState);
        Assert.NotNull(limiter);

        speedLimitState.SetDownloadSpeedLimitMbPerSecond(0);
        await limiter!.ThrottleAsync(1024 * 1024, CancellationToken.None);

        speedLimitState.SetDownloadSpeedLimitMbPerSecond(1);
        await limiter.ThrottleAsync(1024 * 1024, CancellationToken.None);

        var throttledStopwatch = Stopwatch.StartNew();
        await limiter.ThrottleAsync(1024 * 1024, CancellationToken.None);
        throttledStopwatch.Stop();

        speedLimitState.SetDownloadSpeedLimitMbPerSecond(0);
        var unlimitedStopwatch = Stopwatch.StartNew();
        await limiter.ThrottleAsync(1024 * 1024, CancellationToken.None);
        unlimitedStopwatch.Stop();

        Assert.True(throttledStopwatch.Elapsed >= TimeSpan.FromMilliseconds(850));
        Assert.True(unlimitedStopwatch.Elapsed < TimeSpan.FromMilliseconds(250));
    }

    private sealed class TestDownloadSpeedLimitState : IDownloadSpeedLimitState
    {
        public int DownloadSpeedLimitMbPerSecond { get; private set; }

        public void SetDownloadSpeedLimitMbPerSecond(int downloadSpeedLimitMbPerSecond)
        {
            DownloadSpeedLimitMbPerSecond = Math.Max(downloadSpeedLimitMbPerSecond, 0);
        }
    }
}
