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
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ImportConcurrencyLimiterTests
{
    [Fact]
    public async Task MetadataSlotsLimitConcurrencyToTwo()
    {
        var limiter = new ImportConcurrencyLimiter();

        var maxConcurrency = await MeasureMaxConcurrencyAsync(
            limiter.AcquireMetadataSlotAsync,
            requestCount: 5);

        Assert.Equal(2, maxConcurrency);
    }

    [Fact]
    public async Task ModpackDownloadSlotsLimitConcurrencyToFour()
    {
        var limiter = new ImportConcurrencyLimiter();

        var maxConcurrency = await MeasureMaxConcurrencyAsync(
            limiter.AcquireModpackDownloadSlotAsync,
            requestCount: 7);

        Assert.Equal(4, maxConcurrency);
    }

    [Fact]
    public async Task RuntimeDownloadSlotsLimitConcurrencyToEight()
    {
        var limiter = new ImportConcurrencyLimiter();

        var maxConcurrency = await MeasureMaxConcurrencyAsync(
            limiter.AcquireRuntimeDownloadSlotAsync,
            requestCount: 12);

        Assert.Equal(8, maxConcurrency);
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
