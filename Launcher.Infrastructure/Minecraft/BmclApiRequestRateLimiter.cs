/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Smooths launcher-wide requests to the BMCLAPI entry point without consuming
/// the global download budget while a request is waiting for admission.
/// </summary>
internal sealed class BmclApiRequestRateLimiter
{
    internal static readonly TimeSpan DefaultRequestInterval = TimeSpan.FromMilliseconds(50);

    public static BmclApiRequestRateLimiter Shared { get; } = new();

    private const string BmclApiHost = "bmclapi2.bangbang93.com";
    private readonly SemaphoreSlim admissionGate = new(1, 1);
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan requestInterval;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> delayAsync;
    private DateTimeOffset nextRequestAt = DateTimeOffset.MinValue;

    public BmclApiRequestRateLimiter(
        TimeSpan? requestInterval = null,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, ValueTask>? delayAsync = null)
    {
        this.requestInterval = requestInterval ?? DefaultRequestInterval;
        if (this.requestInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestInterval));

        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.delayAsync = delayAsync ?? ((delay, cancellationToken) =>
            new ValueTask(Task.Delay(delay, cancellationToken)));
    }

    public async ValueTask WaitAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!IsBmclApiEntry(uri) || requestInterval == TimeSpan.Zero)
            return;

        await admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            var delay = nextRequestAt - now;
            if (delay > TimeSpan.Zero)
                await delayAsync(delay, cancellationToken).ConfigureAwait(false);

            now = timeProvider.GetUtcNow();
            var admittedAt = now > nextRequestAt ? now : nextRequestAt;
            nextRequestAt = admittedAt + requestInterval;
        }
        finally
        {
            admissionGate.Release();
        }
    }

    internal static bool IsBmclApiEntry(Uri uri) =>
        uri.IsAbsoluteUri
        && string.Equals(uri.IdnHost, BmclApiHost, StringComparison.OrdinalIgnoreCase);
}
