/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Resources;

/// <summary>
/// Infrastructure-only capability used to surface file-transfer progress without
/// changing the Application catalog contract consumed by callers and test fakes.
/// </summary>
internal interface IResourceCatalogProgressReporter
{
    Task<string> InstallProjectVersionWithProgressAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken);

    Task<string> DownloadProjectVersionWithProgressAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken);
}
