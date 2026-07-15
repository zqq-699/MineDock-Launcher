/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Domain.Models;

/// <summary>
/// A task-scoped effective download-throughput update. A null rate explicitly
/// clears a previously displayed value; a null telemetry object means that the
/// progress report is not download telemetry.
/// </summary>
public sealed record DownloadSpeedTelemetry(long? BytesPerSecond)
{
    public static DownloadSpeedTelemetry Clear { get; } = new((long?)null);
}
