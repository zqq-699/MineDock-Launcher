/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public sealed record TerracottaModule(
    string Version,
    string Architecture,
    string DirectoryPath,
    string ExecutablePath);

public interface ITerracottaProvisioningService
{
    TerracottaModule? TryGetAvailable();

    Task<TerracottaModule> EnsureAvailableAsync(
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
