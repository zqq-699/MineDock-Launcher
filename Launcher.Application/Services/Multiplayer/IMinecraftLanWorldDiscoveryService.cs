/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public sealed record MinecraftLanWorld(
    string Name,
    string HostAddress,
    int Port);

public interface IMinecraftLanWorldDiscoveryService
{
    Task<IReadOnlyList<MinecraftLanWorld>> DiscoverLocalWorldsAsync(
        CancellationToken cancellationToken = default,
        IProgress<MinecraftLanWorld>? progress = null);
}
