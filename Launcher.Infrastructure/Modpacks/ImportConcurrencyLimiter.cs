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

    public ValueTask<IAsyncDisposable> AcquireMetadataSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(metadataSemaphore, cancellationToken);
    }

    public ValueTask<IAsyncDisposable> AcquireModpackDownloadSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(modpackDownloadSemaphore, cancellationToken);
    }

    public ValueTask<IAsyncDisposable> AcquireRuntimeDownloadSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(runtimeDownloadSemaphore, cancellationToken);
    }

    public ValueTask<IAsyncDisposable> AcquireHashSlotAsync(CancellationToken cancellationToken = default)
    {
        return AcquireAsync(hashSemaphore, cancellationToken);
    }

    private static async ValueTask<IAsyncDisposable> AcquireAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreLease(semaphore);
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private SemaphoreSlim? semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref semaphore, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
