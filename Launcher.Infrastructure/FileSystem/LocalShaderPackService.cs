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
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalShaderPackService : ILocalShaderPackService
{
    private const string SupportedArchiveExtension = ".zip";
    private readonly ILogger<LocalShaderPackService> logger;

    public LocalShaderPackService(ILogger<LocalShaderPackService>? logger = null)
    {
        this.logger = logger ?? NullLogger<LocalShaderPackService>.Instance;
    }

    public Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Task.Run<IReadOnlyList<LocalShaderPack>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var shaderPacksDirectory = GetShaderPacksDirectory(instance);
                if (!Directory.Exists(shaderPacksDirectory))
                {
                    logger.LogInformation(
                        "No local shader packs directory found. InstanceId={InstanceId} ShaderPacksDirectory={ShaderPacksDirectory}",
                        instance.Id,
                        shaderPacksDirectory);
                    return [];
                }

                var shaderPacks = Directory.EnumerateFiles(
                        shaderPacksDirectory,
                        $"*{SupportedArchiveExtension}",
                        SearchOption.TopDirectoryOnly)
                    .Select(ToLocalShaderPack)
                    .OrderByDescending(shaderPack => shaderPack.CreatedAt)
                    .ThenBy(shaderPack => shaderPack.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                logger.LogInformation(
                    "Local shader packs loaded. InstanceId={InstanceId} Count={ShaderPackCount}",
                    instance.Id,
                    shaderPacks.Length);
                return shaderPacks;
            },
            cancellationToken);
    }

    public Task<LocalShaderPackImportResult> ImportAsync(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return Task.Run(
            () => ImportCore(instance, archivePath, cancellationToken),
            cancellationToken);
    }

    public Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shaderPack);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(shaderPack.FullPath))
                {
                    logger.LogInformation(
                        "Skipping local shader pack delete because file does not exist. Path={Path}",
                        shaderPack.FullPath);
                    return;
                }

                File.Delete(shaderPack.FullPath);
                logger.LogInformation("Local shader pack deleted. Path={Path}", shaderPack.FullPath);
            },
            cancellationToken);
    }

    public Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shaderPacks);

        return Task.Run(
            async () =>
            {
                foreach (var shaderPack in shaderPacks.DistinctBy(shaderPack => shaderPack.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DeleteAsync(shaderPack, cancellationToken);
                }
            },
            cancellationToken);
    }

    private LocalShaderPackImportResult ImportCore(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
        {
            logger.LogInformation(
                "Skipping local shader pack import because archive does not exist. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.FileNotFound);
        }

        if (!normalizedArchivePath.EndsWith(SupportedArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Skipping local shader pack import because archive type is unsupported. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnsupportedArchive);
        }

        logger.LogInformation(
            "Importing local shader pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
            instance.Id,
            normalizedArchivePath);

        try
        {
            var shaderPacksDirectory = GetShaderPacksDirectory(instance);
            Directory.CreateDirectory(shaderPacksDirectory);

            var targetPath = ResolveUniqueFilePath(shaderPacksDirectory, Path.GetFileName(normalizedArchivePath));
            File.Copy(normalizedArchivePath, targetPath, overwrite: false);

            var importedShaderPack = ToLocalShaderPack(targetPath);
            logger.LogInformation(
                "Local shader pack archive imported. InstanceId={InstanceId} ArchivePath={ArchivePath} ShaderPackPath={ShaderPackPath}",
                instance.Id,
                normalizedArchivePath,
                targetPath);
            return LocalShaderPackImportResult.Success(importedShaderPack);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local shader pack archive because a file operation failed. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local shader pack archive because access was denied. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected failure while importing local shader pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
    }

    private static LocalShaderPack ToLocalShaderPack(string path)
    {
        var file = new FileInfo(path);
        return new LocalShaderPack
        {
            Name = Path.GetFileNameWithoutExtension(file.Name),
            FileName = file.Name,
            FullPath = file.FullName,
            CreatedAt = new DateTimeOffset(file.CreationTimeUtc)
        };
    }

    private static string ResolveUniqueFilePath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var index = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
            index++;
        }

        return candidate;
    }

    private static string GetShaderPacksDirectory(GameInstance instance)
    {
        return Path.Combine(instance.InstanceDirectory, "shaderpacks");
    }
}
