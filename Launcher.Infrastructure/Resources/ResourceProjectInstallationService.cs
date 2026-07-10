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
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Resources;

public sealed class ResourceProjectInstallationService : IResourceProjectInstallationService
{
    private readonly IResourceCatalogService resourceCatalogService;
    private readonly ILocalModpackImportService localModpackImportService;
    private readonly ILogger<ResourceProjectInstallationService> logger;

    public ResourceProjectInstallationService(
        IResourceCatalogService resourceCatalogService,
        ILocalModpackImportService localModpackImportService,
        ILogger<ResourceProjectInstallationService> logger)
    {
        this.resourceCatalogService = resourceCatalogService;
        this.localModpackImportService = localModpackImportService;
        this.logger = logger;
    }

    public async Task<ResourceProjectInstallationPreparationResult> PrepareAsync(
        ResourceProjectInstallationRequest request,
        CancellationToken cancellationToken = default)
    {
        var targetExists = request.TargetKind switch
        {
            ResourceProjectInstallationTargetKind.LocalDirectory =>
                await resourceCatalogService.ProjectVersionDownloadExistsAsync(
                    request.Version,
                    RequireTargetDirectory(request),
                    cancellationToken).ConfigureAwait(false),
            ResourceProjectInstallationTargetKind.ExistingInstance =>
                await resourceCatalogService.ProjectVersionInstallExistsAsync(
                    request.Version,
                    RequireInstance(request),
                    cancellationToken).ConfigureAwait(false),
            ResourceProjectInstallationTargetKind.NewModpackInstance => false,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        return new ResourceProjectInstallationPreparationResult(targetExists);
    }

    public async Task<ResourceProjectInstallationResult> ExecuteAsync(
        ResourceProjectInstallationRequest request,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        switch (request.TargetKind)
        {
            case ResourceProjectInstallationTargetKind.LocalDirectory:
            {
                var path = await resourceCatalogService.DownloadProjectVersionAsync(
                    request.Version,
                    RequireTargetDirectory(request),
                    cancellationToken).ConfigureAwait(false);
                return new ResourceProjectInstallationResult(InstalledPath: path);
            }
            case ResourceProjectInstallationTargetKind.ExistingInstance:
            {
                var path = await resourceCatalogService.InstallProjectVersionAsync(
                    request.Version,
                    RequireInstance(request),
                    cancellationToken).ConfigureAwait(false);
                return new ResourceProjectInstallationResult(InstalledPath: path);
            }
            case ResourceProjectInstallationTargetKind.NewModpackInstance:
                return await ImportModpackAsNewInstanceAsync(request.Version, progress, cancellationToken)
                    .ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private async Task<ResourceProjectInstallationResult> ImportModpackAsNewInstanceAsync(
        ResourceProjectVersion version,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"launcher-modpack-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var archivePath = await resourceCatalogService.DownloadProjectVersionAsync(
                version,
                tempDirectory,
                cancellationToken).ConfigureAwait(false);
            var result = await localModpackImportService.ImportFromArchiveAsync(
                archivePath,
                progress,
                cancellationToken).ConfigureAwait(false);
            return new ResourceProjectInstallationResult(archivePath, result);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to clean resource project installation workspace. Workspace={Workspace}",
                    tempDirectory);
            }
        }
    }

    private static string RequireTargetDirectory(ResourceProjectInstallationRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.TargetDirectory)
            ? request.TargetDirectory
            : throw new ArgumentException("A target directory is required.", nameof(request));
    }

    private static GameInstance RequireInstance(ResourceProjectInstallationRequest request)
    {
        return request.Instance ?? throw new ArgumentException("An instance is required.", nameof(request));
    }
}
