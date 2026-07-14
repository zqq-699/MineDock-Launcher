/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Caps simultaneous response-body consumers for one resolved host. The global
/// scheduler remains the primary budget; this only prevents one redirected
/// mirror node from consuming all of it.
/// </summary>
internal sealed class DownloadHostConcurrencyLimiter
{
    public static DownloadHostConcurrencyLimiter Shared { get; } = new();

    private const int PerHostLimit = 6;
    private readonly ConcurrentDictionary<string, HostGate> hosts = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<DownloadHostLease> AcquireAsync(string host, CancellationToken cancellationToken)
    {
        var gate = hosts.GetOrAdd(string.IsNullOrWhiteSpace(host) ? "<unknown>" : host, static _ => new HostGate());
        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var active = Interlocked.Increment(ref gate.ActiveCount);
        return new DownloadHostLease(gate, active);
    }

    internal sealed class DownloadHostLease(HostGate gate, int activeCount) : IDisposable, IAsyncDisposable
    {
        private HostGate? gate = gate;
        public int ActiveCount { get; } = activeCount;

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

        public void Dispose()
        {
            var ownedGate = Interlocked.Exchange(ref gate, null);
            if (ownedGate is null)
                return;
            Interlocked.Decrement(ref ownedGate.ActiveCount);
            ownedGate.Semaphore.Release();
        }
    }

    internal sealed class HostGate
    {
        public SemaphoreSlim Semaphore { get; } = new(PerHostLimit, PerHostLimit);
        public int ActiveCount;
    }
}
