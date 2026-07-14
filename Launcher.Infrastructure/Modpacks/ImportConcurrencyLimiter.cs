/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ImportConcurrencyLimiter : IImportConcurrencyLimiter
{
    public static ImportConcurrencyLimiter Shared { get; } = new();

    private readonly SemaphoreSlim hashSemaphore = new(2, 2);
    private readonly AdaptiveDownloadScheduler downloadScheduler = new(minimum: 4, initial: 12, maximum: 16);

    public ValueTask<IImportConcurrencyLease> AcquireMetadataSlotAsync(CancellationToken cancellationToken = default) =>
        downloadScheduler.AcquireAsync(cancellationToken);

    public ValueTask<IImportConcurrencyLease> AcquireModpackDownloadSlotAsync(CancellationToken cancellationToken = default) =>
        downloadScheduler.AcquireAsync(cancellationToken);

    public ValueTask<IImportConcurrencyLease> AcquireRuntimeDownloadSlotAsync(CancellationToken cancellationToken = default) =>
        downloadScheduler.AcquireAsync(cancellationToken);

    public ValueTask<IImportConcurrencyLease> AcquireHashSlotAsync(CancellationToken cancellationToken = default) =>
        AcquireFixedAsync(hashSemaphore, cancellationToken);

    internal void RecordDownloadResult(DownloadConcurrencyCategory category, DownloadFailureReason? failureReason)
    {
        _ = category;
        downloadScheduler.RecordResult(failureReason);
    }

    internal (int ActiveCount, int WaitingCount, int CurrentTarget, int ConfiguredMaximum) DownloadSnapshot =>
        downloadScheduler.Snapshot;

    private static async ValueTask<IImportConcurrencyLease> AcquireFixedAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreLease(semaphore);
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IImportConcurrencyLease
    {
        private SemaphoreSlim? semaphore = semaphore;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public void Dispose() => Interlocked.Exchange(ref semaphore, null)?.Release();
    }
}

/// <summary>
/// A lease scheduler instead of a replaceable semaphore. Lowering the target
/// simply stops issuing new leases; already useful requests finish normally.
/// </summary>
internal sealed class AdaptiveDownloadScheduler
{
    private static readonly TimeSpan AdjustmentCooldown = TimeSpan.FromSeconds(20);
    private readonly object syncRoot = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly int minimum;
    private readonly int maximum;
    private int activeCount;
    private int waitingCount;
    private int currentTarget;
    private int successes;
    private int failures;
    private DateTimeOffset lastAdjustmentAt = DateTimeOffset.UtcNow;

    public AdaptiveDownloadScheduler(int minimum, int initial, int maximum)
    {
        this.minimum = minimum;
        this.maximum = maximum;
        currentTarget = initial;
    }

    internal (int ActiveCount, int WaitingCount, int CurrentTarget, int ConfiguredMaximum) Snapshot
    {
        get { lock (syncRoot) return (activeCount, waitingCount, currentTarget, maximum); }
    }

    public async ValueTask<IImportConcurrencyLease> AcquireAsync(CancellationToken cancellationToken)
    {
        lock (syncRoot)
        {
            waitingCount++;
        }
        try
        {
            while (true)
            {
                lock (syncRoot)
                {
                    if (activeCount < currentTarget)
                    {
                        activeCount++;
                        return new Lease(this);
                    }
                }
                await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (syncRoot)
            {
                waitingCount--;
            }
        }
    }

    public void RecordResult(DownloadFailureReason? failureReason)
    {
        lock (syncRoot)
        {
            if (failureReason is null)
                successes++;
            else if (failureReason is DownloadFailureReason.Network or DownloadFailureReason.Dns
                or DownloadFailureReason.ResponseHeadersTimeout or DownloadFailureReason.FirstByteTimeout
                or DownloadFailureReason.BodyIdleTimeout or DownloadFailureReason.SustainedLowSpeed
                or DownloadFailureReason.BodyInterrupted
                or DownloadFailureReason.HttpStatus)
                failures++;

            var now = DateTimeOffset.UtcNow;
            if (now - lastAdjustmentAt < AdjustmentCooldown)
                return;
            if (failures > 0)
                currentTarget = Math.Max(minimum, (currentTarget + 1) / 2);
            else if (successes >= currentTarget && waitingCount > 0)
                currentTarget = Math.Min(maximum, currentTarget + 1);
            successes = 0;
            failures = 0;
            lastAdjustmentAt = now;
            SignalWaiters();
        }
    }

    private void Release()
    {
        lock (syncRoot)
        {
            activeCount--;
            SignalWaiters();
        }
    }

    private void SignalWaiters()
    {
        var available = Math.Max(0, currentTarget - activeCount);
        while (available-- > 0 && signal.CurrentCount < waitingCount)
            signal.Release();
    }

    private sealed class Lease(AdaptiveDownloadScheduler owner) : IImportConcurrencyLease
    {
        private AdaptiveDownloadScheduler? owner = owner;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
}
