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

using System.IO;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

public sealed class ModpackWorkspaceCleanupService : IModpackWorkspaceCleanupService
{
    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<ModpackWorkspaceCleanupService> logger;

    public ModpackWorkspaceCleanupService(
        LauncherPathProvider pathProvider,
        ILogger<ModpackWorkspaceCleanupService>? logger = null)
    {
        this.pathProvider = pathProvider;
        this.logger = logger ?? NullLogger<ModpackWorkspaceCleanupService>.Instance;
    }

    public Task CleanupAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CleanupAll(cancellationToken), cancellationToken);
    }

    private void CleanupAll(CancellationToken cancellationToken)
    {
        var modpackCacheDirectory = Path.Combine(pathProvider.DefaultDataDirectory, "cache", "modpacks");
        logger.LogDebug(
            "Cleaning modpack workspace cache. CacheDirectory={CacheDirectory}",
            modpackCacheDirectory);

        if (!Directory.Exists(modpackCacheDirectory))
        {
            logger.LogDebug(
                "Modpack workspace cache cleanup completed. CacheDirectory={CacheDirectory} DeletedCount={DeletedCount} FailedCount={FailedCount}",
                modpackCacheDirectory,
                0,
                0);
            return;
        }

        var deletedCount = 0;
        var failedCount = 0;

        foreach (var workspaceDirectory in Directory.EnumerateDirectories(modpackCacheDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Directory.Delete(workspaceDirectory, recursive: true);
                deletedCount++;
            }
            catch (IOException exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete modpack workspace cache directory. Directory={Directory}",
                    workspaceDirectory);
            }
            catch (UnauthorizedAccessException exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete modpack workspace cache directory. Directory={Directory}",
                    workspaceDirectory);
            }
        }

        if (failedCount > 0)
        {
            logger.LogWarning(
                "Modpack workspace cache cleanup completed with failures. DeletedCount={DeletedCount} FailedCount={FailedCount}",
                deletedCount,
                failedCount);
        }
        else if (deletedCount > 0)
        {
            logger.LogInformation(
                "Modpack workspace cache cleaned. DeletedCount={DeletedCount}",
                deletedCount);
        }
        else
        {
            logger.LogDebug("Modpack workspace cache was already clean.");
        }
    }
}
