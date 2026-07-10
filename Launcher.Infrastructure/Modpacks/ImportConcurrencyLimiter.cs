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

using System.Threading;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ImportConcurrencyLimiter : IImportConcurrencyLimiter
{
    public static ImportConcurrencyLimiter Shared { get; } = new();

    private readonly SemaphoreSlim metadataSemaphore = new(2, 2);
    private readonly SemaphoreSlim modpackDownloadSemaphore = new(4, 4);
    private readonly SemaphoreSlim runtimeDownloadSemaphore = new(8, 8);
    private readonly SemaphoreSlim hashSemaphore = new(2, 2);

    public ValueTask<IImportConcurrencyLease> AcquireMetadataSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(metadataSemaphore, cancellationToken);
    }

    public ValueTask<IImportConcurrencyLease> AcquireModpackDownloadSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(modpackDownloadSemaphore, cancellationToken);
    }

    public ValueTask<IImportConcurrencyLease> AcquireRuntimeDownloadSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(runtimeDownloadSemaphore, cancellationToken);
    }

    public ValueTask<IImportConcurrencyLease> AcquireHashSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(hashSemaphore, cancellationToken);
    }

    private static async ValueTask<IImportConcurrencyLease> AcquireAsync(
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreLease(semaphore);
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IImportConcurrencyLease
    {
        private SemaphoreSlim? semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref semaphore, null)?.Release();
        }
    }
}
