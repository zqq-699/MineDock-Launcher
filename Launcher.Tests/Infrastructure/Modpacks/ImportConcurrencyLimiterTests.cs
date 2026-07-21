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
