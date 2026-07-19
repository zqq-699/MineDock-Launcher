/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public sealed record EasyTierModule(
    string Version,
    string DirectoryPath,
    string CoreExecutablePath,
    string CliExecutablePath,
    string PacketLibraryPath);

public interface IEasyTierProvisioningService
{
    EasyTierModule? TryGetAvailable();

    Task<EasyTierModule> EnsureAvailableAsync(
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
