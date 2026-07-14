/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>Small, observational host health window; it does not change source preference.</summary>
internal sealed class DownloadHostHealthTracker
{
    public static DownloadHostHealthTracker Shared { get; } = new();

    private readonly ConcurrentDictionary<(string Source, string Host), HostState> states = new();
    private readonly TimeSpan cooldown = TimeSpan.FromSeconds(45);

    public bool ShouldAvoid(string resolvedSourceKind, string host)
    {
        return states.TryGetValue((resolvedSourceKind, host), out var state)
            && state.AvoidUntil > DateTimeOffset.UtcNow;
    }

    public void RecordSuccess(string resolvedSourceKind, string host)
    {
        states.TryRemove((resolvedSourceKind, host), out _);
    }

    public void RecordFailure(
        string resolvedSourceKind,
        string host,
        DownloadFailureReason reason,
        System.Net.HttpStatusCode? statusCode = null)
    {
        if (!AffectsHealth(reason, statusCode))
            return;
        states.AddOrUpdate(
            (resolvedSourceKind, host),
            _ => CreateFailureState(reason, 1, DateTimeOffset.MinValue),
            (_, current) =>
            {
                var failures = current.Failures + 1;
                return CreateFailureState(reason, failures, current.AvoidUntil);
            });
    }

    internal static bool AffectsHealth(DownloadFailureReason reason, System.Net.HttpStatusCode? statusCode = null) => reason is
        DownloadFailureReason.Dns or DownloadFailureReason.Network or DownloadFailureReason.ResponseHeadersTimeout
        or DownloadFailureReason.FirstByteTimeout or DownloadFailureReason.BodyIdleTimeout
        or DownloadFailureReason.SustainedLowSpeed or DownloadFailureReason.BodyInterrupted
        || (reason is DownloadFailureReason.HttpStatus && statusCode is { } code && (int)code is >= 500 and <= 599);

    private HostState CreateFailureState(DownloadFailureReason reason, int failures, DateTimeOffset avoidUntil)
    {
        if (reason is DownloadFailureReason.SustainedLowSpeed || failures >= 3)
            avoidUntil = DateTimeOffset.UtcNow + cooldown;
        return new HostState(failures, avoidUntil);
    }

    private sealed record HostState(int Failures, DateTimeOffset AvoidUntil);
}
