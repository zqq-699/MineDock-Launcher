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
    public async Task GlobalSchedulerStartsAtSixtyFourWithinSupportedMaximum()
    {
        var scheduler = new FixedDownloadScheduler(
            ImportConcurrencyLimiter.MaximumDownloadConcurrency,
            ImportConcurrencyLimiter.InitialDownloadConcurrency);
        var leases = new List<IImportConcurrencyLease>();

        try
        {
            for (var index = 0; index < ImportConcurrencyLimiter.InitialDownloadConcurrency; index++)
                leases.Add(await scheduler.AcquireAsync(CancellationToken.None));

            Assert.Equal(ImportConcurrencyLimiter.InitialDownloadConcurrency, scheduler.Snapshot.CurrentTarget);
            Assert.Equal(ImportConcurrencyLimiter.InitialDownloadConcurrency, scheduler.Snapshot.ConfiguredMaximum);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    [Fact]
    public async Task LowerLimitWaitsForExistingLeasesBeforeGrantingQueuedRequest()
    {
        var scheduler = new FixedDownloadScheduler(maximum: 4, initial: 2);
        var first = await scheduler.AcquireAsync(CancellationToken.None);
        var second = await scheduler.AcquireAsync(CancellationToken.None);
        scheduler.SetMaximum(1);
        var queued = scheduler.AcquireAsync(CancellationToken.None).AsTask();

        first.Dispose();
        Assert.False(queued.IsCompleted);

        second.Dispose();
        await using var released = await queued.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, scheduler.Snapshot.CurrentTarget);
        Assert.Equal(1, scheduler.Snapshot.ActiveCount);
    }

    [Fact]
    public async Task RaisingLimitImmediatelyGrantsWaitingRequests()
    {
        var scheduler = new FixedDownloadScheduler(maximum: 4, initial: 1);
        await using var first = await scheduler.AcquireAsync(CancellationToken.None);
        var second = scheduler.AcquireAsync(CancellationToken.None).AsTask();
        var third = scheduler.AcquireAsync(CancellationToken.None).AsTask();

        scheduler.SetMaximum(3);

        await using var secondLease = await second.WaitAsync(TimeSpan.FromSeconds(1));
        await using var thirdLease = await third.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(3, scheduler.Snapshot.ActiveCount);
        Assert.Equal(3, scheduler.Snapshot.CurrentTarget);
    }

    [Fact]
    public async Task CanceledQueuedRequestDoesNotConsumeCapacity()
    {
        var scheduler = new FixedDownloadScheduler(maximum: 1);
        var first = await scheduler.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var canceled = scheduler.AcquireAsync(cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        first.Dispose();

        await using var replacement = await scheduler
            .AcquireAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, scheduler.Snapshot.ActiveCount);
        Assert.Equal(0, scheduler.Snapshot.WaitingCount);
    }

    [Fact]
    public async Task GlobalSchedulerReleasesQueuedRequestsWithoutChangingTarget()
    {
        var scheduler = new FixedDownloadScheduler(maximum: 2);
        var first = await scheduler.AcquireAsync(CancellationToken.None);
        var second = await scheduler.AcquireAsync(CancellationToken.None);

        try
        {
            var queued = scheduler.AcquireAsync(CancellationToken.None).AsTask();
            Assert.False(queued.IsCompleted);
            first.Dispose();
            await using var released = await queued.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(2, scheduler.Snapshot.CurrentTarget);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
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

}
