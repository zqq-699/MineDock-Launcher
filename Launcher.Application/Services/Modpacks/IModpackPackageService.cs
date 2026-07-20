/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModpackPackageService
{
    Task<ModpackRecognitionResult> RecognizeAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<PreparedModpack> PrepareAsync(
        string archivePath,
        CancellationToken cancellationToken = default,
        IProgress<LauncherProgress>? progress = null);

    Task<PreparedModpack> PrepareAsync(
        string archivePath,
        ModpackInstallEnvironment environment,
        CancellationToken cancellationToken = default,
        IProgress<LauncherProgress>? progress = null) =>
        environment is ModpackInstallEnvironment.Client
            ? PrepareAsync(archivePath, cancellationToken, progress)
            : throw new NotSupportedException("Server modpack preparation is not supported by this package service.");

    Task<IReadOnlyList<ManualModpackDownload>> DownloadFilesAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
        int downloadSpeedLimitMbPerSecond = 0);

    Task CopyOverridesAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default);

    Task<string?> WriteManualDownloadsFileAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IReadOnlyList<ManualModpackDownload> manualDownloads,
        CancellationToken cancellationToken = default);

    Task InstallContentAsync(
        PreparedModpack preparedModpack,
        GameInstance instance,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
        int downloadSpeedLimitMbPerSecond = 0);

    Task CleanupAsync(
        PreparedModpack preparedModpack,
        CancellationToken cancellationToken = default);
}
