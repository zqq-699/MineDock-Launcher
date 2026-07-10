/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class DownloadBandwidthLimiterTests
{
    [Fact]
    public async Task SeparateFixedLimitersShareConcurrentReservationsAsOneBudget()
    {
        var firstLimiter = DownloadBandwidthLimiter.Create(1);
        var secondLimiter = DownloadBandwidthLimiter.Create(1);
        Assert.NotNull(firstLimiter);
        Assert.NotNull(secondLimiter);

        var payloadBytes = 1024 * 1024;
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(
            firstLimiter!.ThrottleAsync(payloadBytes, CancellationToken.None).AsTask(),
            secondLimiter!.ThrottleAsync(payloadBytes, CancellationToken.None).AsTask());

        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(850));
    }

    [Fact]
    public async Task SeparateStateBackedLimitersShareConcurrentReservationsAsOneBudget()
    {
        var speedLimitState = new TestDownloadSpeedLimitState();
        speedLimitState.SetDownloadSpeedLimitMbPerSecond(1);
        var firstLimiter = DownloadBandwidthLimiter.Create(0, speedLimitState);
        var secondLimiter = DownloadBandwidthLimiter.Create(0, speedLimitState);
        Assert.NotNull(firstLimiter);
        Assert.NotNull(secondLimiter);

        var payloadBytes = 1024 * 1024;
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(
            firstLimiter!.ThrottleAsync(payloadBytes, CancellationToken.None).AsTask(),
            secondLimiter!.ThrottleAsync(payloadBytes, CancellationToken.None).AsTask());

        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(850));
    }

    [Fact]
    public void CreateReturnsNullWhenEffectiveLimitIsDisabledAtRequestCreation()
    {
        var speedLimitState = new TestDownloadSpeedLimitState();
        speedLimitState.SetDownloadSpeedLimitMbPerSecond(0);

        var limiter = DownloadBandwidthLimiter.Create(4, speedLimitState);

        Assert.Null(limiter);
    }

    [Fact]
    public async Task ExistingStateBackedLimiterRespectsLaterDisableAndReenable()
    {
        var speedLimitState = new TestDownloadSpeedLimitState();
        speedLimitState.SetDownloadSpeedLimitMbPerSecond(1);
        var limiter = DownloadBandwidthLimiter.Create(0, speedLimitState);
        Assert.NotNull(limiter);

        await limiter!.ThrottleAsync(1024 * 1024, CancellationToken.None);

        var throttledStopwatch = Stopwatch.StartNew();
        await limiter.ThrottleAsync(1024 * 1024, CancellationToken.None);
        throttledStopwatch.Stop();

        speedLimitState.SetDownloadSpeedLimitMbPerSecond(0);
        var unlimitedStopwatch = Stopwatch.StartNew();
        await limiter.ThrottleAsync(1024 * 1024, CancellationToken.None);
        unlimitedStopwatch.Stop();

        speedLimitState.SetDownloadSpeedLimitMbPerSecond(1);
        await limiter.ThrottleAsync(1024 * 1024, CancellationToken.None);

        var reenabledStopwatch = Stopwatch.StartNew();
        await limiter.ThrottleAsync(1024 * 1024, CancellationToken.None);
        reenabledStopwatch.Stop();

        Assert.True(throttledStopwatch.Elapsed >= TimeSpan.FromMilliseconds(850));
        Assert.True(unlimitedStopwatch.Elapsed < TimeSpan.FromMilliseconds(250));
        Assert.True(reenabledStopwatch.Elapsed >= TimeSpan.FromMilliseconds(850));
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
