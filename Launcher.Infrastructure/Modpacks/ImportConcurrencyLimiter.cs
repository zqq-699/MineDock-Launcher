/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
namespace Launcher.Infrastructure.Modpacks;

internal sealed class ImportConcurrencyLimiter : IImportConcurrencyLimiter
{
    internal const int MinimumDownloadConcurrency = 4;
    internal const int InitialDownloadConcurrency = 64;
    internal const int MaximumDownloadConcurrency = 64;

    public static ImportConcurrencyLimiter Shared { get; } = new();

    private readonly SemaphoreSlim hashSemaphore = new(2, 2);
    private readonly FixedDownloadScheduler downloadScheduler = new(MaximumDownloadConcurrency);

    public ValueTask<IImportConcurrencyLease> AcquireMetadataSlotAsync(CancellationToken cancellationToken = default) =>
        downloadScheduler.AcquireAsync(cancellationToken);

    public ValueTask<IImportConcurrencyLease> AcquireModpackDownloadSlotAsync(CancellationToken cancellationToken = default) =>
        downloadScheduler.AcquireAsync(cancellationToken);

    public ValueTask<IImportConcurrencyLease> AcquireRuntimeDownloadSlotAsync(CancellationToken cancellationToken = default) =>
        downloadScheduler.AcquireAsync(cancellationToken);

    public ValueTask<IImportConcurrencyLease> AcquireHashSlotAsync(CancellationToken cancellationToken = default) =>
        AcquireFixedAsync(hashSemaphore, cancellationToken);

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

internal sealed class FixedDownloadScheduler
{
    private readonly SemaphoreSlim semaphore;
    private readonly int maximum;
    private int activeCount;
    private int waitingCount;

    public FixedDownloadScheduler(int maximum)
    {
        this.maximum = maximum;
        semaphore = new SemaphoreSlim(maximum, maximum);
    }

    internal (int ActiveCount, int WaitingCount, int CurrentTarget, int ConfiguredMaximum) Snapshot
    {
        get => (
            Volatile.Read(ref activeCount),
            Volatile.Read(ref waitingCount),
            maximum,
            maximum);
    }

    public async ValueTask<IImportConcurrencyLease> AcquireAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref waitingCount);
        try
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref waitingCount);
        }
        Interlocked.Increment(ref activeCount);
        return new Lease(this);
    }

    private void Release()
    {
        Interlocked.Decrement(ref activeCount);
        semaphore.Release();
    }

    private sealed class Lease(FixedDownloadScheduler owner) : IImportConcurrencyLease
    {
        private FixedDownloadScheduler? owner = owner;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
}
