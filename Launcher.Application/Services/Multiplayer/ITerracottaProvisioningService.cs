/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public static class TerracottaProjectMetadata
{
    public const string RepositoryUrl = "https://github.com/burningtnt/Terracotta";
    public const string ReferencedVersion = "0.4.2";
}

public static class EasyTierProjectMetadata
{
    public const string RepositoryUrl = "https://github.com/burningtnt/EasyTier/tree/v2.5.0-terracotta.2";
    public const string ReferencedVersion = "v2.5.0-terracotta.2";
}

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
