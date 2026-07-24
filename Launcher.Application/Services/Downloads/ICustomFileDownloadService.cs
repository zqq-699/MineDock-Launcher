/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface ICustomFileDownloadService
{
    Task DownloadAsync(
        string sourceUrl,
        string destinationPath,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
