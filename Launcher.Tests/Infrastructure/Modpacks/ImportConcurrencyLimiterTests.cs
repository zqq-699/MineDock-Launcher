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

using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ImportConcurrencyLimiterTests
{
    [Fact]
    public async Task AdaptiveSchedulerStartsAtSixtyFourAndDropsAfterFailure()
    {
        var clock = new TestTimeProvider();
        var scheduler = new AdaptiveDownloadScheduler(
            ImportConcurrencyLimiter.MinimumDownloadConcurrency,
            ImportConcurrencyLimiter.InitialDownloadConcurrency,
            ImportConcurrencyLimiter.MaximumDownloadConcurrency,
            timeProvider: clock,
            adjustmentCooldown: TimeSpan.FromSeconds(1));
        var leases = new List<IImportConcurrencyLease>();

        try
        {
            for (var index = 0; index < ImportConcurrencyLimiter.InitialDownloadConcurrency; index++)
                leases.Add(await scheduler.AcquireAsync(CancellationToken.None));

            Assert.Equal(ImportConcurrencyLimiter.InitialDownloadConcurrency, scheduler.Snapshot.CurrentTarget);
            Assert.Equal(ImportConcurrencyLimiter.MaximumDownloadConcurrency, scheduler.Snapshot.ConfiguredMaximum);

            clock.Advance(TimeSpan.FromSeconds(1));
            scheduler.RecordResult(DownloadFailureReason.Network);
            Assert.Equal(32, scheduler.Snapshot.CurrentTarget);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    [Fact]
    public async Task AdaptiveSchedulerRampsToMaximumWhenSuccessfulRequestsAreWaiting()
    {
        var rampingClock = new TestTimeProvider();
        var rampingScheduler = new AdaptiveDownloadScheduler(
            minimum: 4,
            initial: 4,
            maximum: 6,
            timeProvider: rampingClock,
            adjustmentCooldown: TimeSpan.FromSeconds(1));
        var rampingLeases = new List<IImportConcurrencyLease>();

        try
        {
            for (var index = 0; index < 4; index++)
                rampingLeases.Add(await rampingScheduler.AcquireAsync(CancellationToken.None));

            for (var target = 4;
                 target < 6;
                 target++)
            {
                var queued = rampingScheduler.AcquireAsync(CancellationToken.None).AsTask();
                Assert.True(SpinWait.SpinUntil(
                    () => rampingScheduler.Snapshot.WaitingCount == 1,
                    TimeSpan.FromSeconds(1)));

                for (var index = 0; index < target; index++)
                    rampingScheduler.RecordResult(failureReason: null);

                rampingClock.Advance(TimeSpan.FromSeconds(1));
                rampingScheduler.RecordResult(failureReason: null);
                rampingLeases.Add(await queued.WaitAsync(TimeSpan.FromSeconds(1)));
                Assert.Equal(target + 1, rampingScheduler.Snapshot.CurrentTarget);
            }
        }
        finally
        {
            foreach (var lease in rampingLeases)
                lease.Dispose();
        }
    }

    [Fact]
    public async Task MetadataSlotsUseTheSharedGlobalBudget()
    {
        var limiter = new ImportConcurrencyLimiter();

        var maxConcurrency = await MeasureMaxConcurrencyAsync(
            limiter.AcquireMetadataSlotAsync,
            requestCount: 5);

        Assert.Equal(5, maxConcurrency);
    }

    [Fact]
    public async Task ModpackDownloadSlotsUseTheSharedGlobalBudget()
    {
        var limiter = new ImportConcurrencyLimiter();

        var maxConcurrency = await MeasureMaxConcurrencyAsync(
            limiter.AcquireModpackDownloadSlotAsync,
            requestCount: 7);

        Assert.Equal(7, maxConcurrency);
    }

    [Fact]
    public async Task RuntimeDownloadSlotsUseTheSharedGlobalBudget()
    {
        var limiter = new ImportConcurrencyLimiter();

        var maxConcurrency = await MeasureMaxConcurrencyAsync(
            limiter.AcquireRuntimeDownloadSlotAsync,
            requestCount: 12);

        Assert.Equal(12, maxConcurrency);
    }

    [Fact]
    public async Task MetadataSlotsDoNotBlockRuntimeDownloads()
    {
        var limiter = new ImportConcurrencyLimiter();
        var metadataLeases = new List<IAsyncDisposable>
        {
            await limiter.AcquireMetadataSlotAsync(),
            await limiter.AcquireMetadataSlotAsync()
        };

        try
        {
            var runtimeAcquireTasks = Enumerable.Range(0, 8)
                .Select(_ => limiter.AcquireRuntimeDownloadSlotAsync().AsTask())
                .ToArray();

            var runtimeLeases = await Task.WhenAll(runtimeAcquireTasks);
            foreach (var lease in runtimeLeases)
                await lease.DisposeAsync();
        }
        finally
        {
            foreach (var lease in metadataLeases)
                await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task SynchronousLeaseDisposalReleasesSlot()
    {
        var limiter = new ImportConcurrencyLimiter();
        var first = await limiter.AcquireMetadataSlotAsync();
        var second = await limiter.AcquireMetadataSlotAsync();

        first.Dispose();
        await using var replacement = await limiter
            .AcquireMetadataSlotAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1));

        await second.DisposeAsync();
    }

    private static async Task<int> MeasureMaxConcurrencyAsync(
        Func<CancellationToken, ValueTask<IImportConcurrencyLease>> acquireAsync,
        int requestCount)
    {
        var activeCount = 0;
        var maxConcurrency = 0;
        var tasks = Enumerable.Range(0, requestCount)
            .Select(async _ =>
            {
                await using var lease = await acquireAsync(CancellationToken.None);
                var current = Interlocked.Increment(ref activeCount);
                UpdateMaxConcurrency(ref maxConcurrency, current);
                try
                {
                    await Task.Delay(80);
                }
                finally
                {
                    Interlocked.Decrement(ref activeCount);
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);
        return Volatile.Read(ref maxConcurrency);
    }

    private static void UpdateMaxConcurrency(ref int maxConcurrency, int current)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref maxConcurrency);
            if (current <= snapshot)
                return;

            if (Interlocked.CompareExchange(ref maxConcurrency, current, snapshot) == snapshot)
                return;
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
