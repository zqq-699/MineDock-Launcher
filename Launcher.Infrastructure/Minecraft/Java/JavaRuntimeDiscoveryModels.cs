/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

internal sealed record JavaRuntimeCandidate(
    string ExecutablePath,
    string Source,
    string IdentityPath);

internal sealed record JavaVersionProbeResult(
    string? Version,
    int? MajorVersion,
    string Architecture);
