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
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Resources;

internal sealed class ResourceProjectStorage
{
    private readonly HttpClient httpClient;
    private readonly ILocalSaveService localSaveService;
    private readonly ILogger logger;

    public ResourceProjectStorage(
        HttpClient httpClient,
        ILocalSaveService localSaveService,
        ILogger logger)
    {
        this.httpClient = httpClient;
        this.localSaveService = localSaveService;
        this.logger = logger;
    }

    public async Task<string> InstallAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            throw new InvalidOperationException("The target instance directory is empty.");
        if (version.Kind is ResourceProjectKind.World)
            return await InstallWorldAsync(version, instance, cancellationToken).ConfigureAwait(false);

        var installDirectory = ResolveInstallDirectory(instance, version.Kind);
        Directory.CreateDirectory(installDirectory);
        return await DownloadCoreAsync(version, installDirectory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DownloadAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new InvalidOperationException("The target download directory is empty.");
        Directory.CreateDirectory(targetDirectory);
        return await DownloadCoreAsync(version, targetDirectory, cancellationToken).ConfigureAwait(false);
    }

    public bool DownloadExists(ResourceProjectVersion version, string targetDirectory)
    {
        return !string.IsNullOrWhiteSpace(targetDirectory)
            && File.Exists(Path.Combine(targetDirectory, ResolveFileName(version)));
    }

    public bool InstallExists(ResourceProjectVersion version, GameInstance instance)
    {
        return version.Kind is not ResourceProjectKind.World
            && !string.IsNullOrWhiteSpace(instance.InstanceDirectory)
            && File.Exists(Path.Combine(
                ResolveInstallDirectory(instance, version.Kind),
                ResolveFileName(version)));
    }

    private async Task<string> DownloadCoreAsync(
        ResourceProjectVersion version,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var target = Path.Combine(targetDirectory, ResolveFileName(version));
        var urls = new[] { version.PrimaryDownloadUrl }
            .Concat(version.FallbackDownloadUrls)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (urls.Length == 0)
            throw new InvalidOperationException($"Resource project version has no download URL: {version.VersionId}");

        Exception? lastException = null;
        foreach (var url in urls)
        {
            try
            {
                await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
                await using var destination = File.Create(target);
                await stream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                return target;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                lastException = exception;
                logger.LogWarning(
                    exception,
                    "Failed to download resource project version candidate. VersionId={VersionId} Url={Url}",
                    version.VersionId,
                    url);
            }
        }

        throw new InvalidOperationException($"Failed to download resource project version: {version.VersionId}", lastException);
    }

    private async Task<string> InstallWorldAsync(
        ResourceProjectVersion version,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"launcher-world-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var archivePath = await DownloadCoreAsync(version, tempDirectory, cancellationToken).ConfigureAwait(false);
            var result = await localSaveService.ImportFromArchiveAsync(instance, archivePath, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.ImportedSave is null)
                throw new InvalidOperationException($"Failed to import world archive. FailureReason={result.FailureReason}");
            return result.ImportedSave.FullPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete temporary resource world directory. Directory={Directory}",
                    tempDirectory);
            }
        }
    }

    private static string ResolveFileName(ResourceProjectVersion version)
    {
        var fileName = Path.GetFileName(version.FileName);
        return string.IsNullOrWhiteSpace(fileName)
            ? $"{version.VersionId}{ResolveDefaultExtension(version.Kind)}"
            : fileName;
    }

    private static string ResolveDefaultExtension(ResourceProjectKind kind)
    {
        return kind switch
        {
            ResourceProjectKind.Modpack => ".mrpack",
            ResourceProjectKind.ResourcePack or ResourceProjectKind.ShaderPack or ResourceProjectKind.World => ".zip",
            _ => ".jar"
        };
    }

    private static string ResolveInstallDirectory(GameInstance instance, ResourceProjectKind kind)
    {
        var directoryName = kind switch
        {
            ResourceProjectKind.ResourcePack => "resourcepacks",
            ResourceProjectKind.ShaderPack => "shaderpacks",
            ResourceProjectKind.World => "saves",
            _ => "mods"
        };
        return Path.Combine(instance.InstanceDirectory, directoryName);
    }
}
