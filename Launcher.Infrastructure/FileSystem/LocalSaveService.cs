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
using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalSaveService : ILocalSaveService
{
    private readonly ILogger<LocalSaveService> logger;
    private readonly LocalSaveArchiveImporter archiveImporter;
    private readonly string iconCacheDirectory;

    public LocalSaveService(LauncherPathProvider? pathProvider = null, ILogger<LocalSaveService>? logger = null)
    {
        var effectivePathProvider = pathProvider ?? new LauncherPathProvider();
        this.logger = logger ?? NullLogger<LocalSaveService>.Instance;
        archiveImporter = new LocalSaveArchiveImporter(this.logger);
        iconCacheDirectory = Path.Combine(effectivePathProvider.DefaultDataDirectory, "cache", "saves", "icons");
    }

    public Task<IReadOnlyList<LocalSave>> GetSavesAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Task.Run<IReadOnlyList<LocalSave>>(
            () => LoadSaves(instance, cancellationToken),
            cancellationToken);
    }

    public Task<LocalSaveImportResult> ImportFromArchiveAsync(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return Task.Run(
            () => archiveImporter.Import(
                instance.Id,
                GetSavesDirectory(instance),
                archivePath,
                ToLocalSave,
                cancellationToken),
            cancellationToken);
    }

    public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        return Task.Run(
            () => DeleteCore(save, cancellationToken),
            cancellationToken);
    }

    public Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saves);
        return DeleteManyAsync(saves, cancellationToken);
    }

    private IReadOnlyList<LocalSave> LoadSaves(GameInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var savesDirectory = GetSavesDirectory(instance);
        if (!Directory.Exists(savesDirectory))
        {
            logger.LogInformation(
                "No local saves directory found. InstanceId={InstanceId} SavesDirectory={SavesDirectory}",
                instance.Id,
                savesDirectory);
            return [];
        }

        var saves = Directory.EnumerateDirectories(savesDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(ToLocalSave)
            .OrderByDescending(save => save.CreatedAt)
            .ThenBy(save => save.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        logger.LogInformation("Local saves loaded. InstanceId={InstanceId} Count={SaveCount}", instance.Id, saves.Length);
        return saves;
    }

    private void DeleteCore(LocalSave save, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(save.FullPath))
        {
            logger.LogInformation("Skipping local save delete because directory does not exist. Path={Path}", save.FullPath);
            return;
        }

        Directory.Delete(save.FullPath, recursive: true);
        logger.LogInformation("Local save deleted. Path={Path}", save.FullPath);
    }

    private async Task DeleteManyAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken)
    {
        foreach (var save in saves.DistinctBy(save => save.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteAsync(save, cancellationToken).ConfigureAwait(false);
        }
    }

    private LocalSave ToLocalSave(string path)
    {
        var directory = new DirectoryInfo(path);
        return new LocalSave
        {
            Name = directory.Name,
            DirectoryName = directory.Name,
            FullPath = directory.FullName,
            IconSource = TryGetCachedIconSource(directory),
            CreatedAt = new DateTimeOffset(directory.CreationTimeUtc)
        };
    }

    private string? TryGetCachedIconSource(DirectoryInfo directory)
    {
        var iconPath = Path.Combine(directory.FullName, "icon.png");
        if (!File.Exists(iconPath))
            return null;

        try
        {
            Directory.CreateDirectory(iconCacheDirectory);
            var iconFile = new FileInfo(iconPath);
            var cachePath = GetCachePath(iconFile);
            if (!File.Exists(cachePath))
                File.Copy(iconFile.FullName, cachePath, overwrite: false);

            return new Uri(cachePath).AbsoluteUri;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            logger.LogWarning(
                exception,
                "Failed to cache local save icon. SaveDirectory={SaveDirectory} IconPath={IconPath}",
                directory.FullName,
                iconPath);
            return null;
        }
    }

    private string GetCachePath(FileInfo iconFile)
    {
        var hashInput = $"{iconFile.FullName}|{iconFile.Length}|{iconFile.LastWriteTimeUtc.Ticks}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(iconCacheDirectory, $"{hash}.png");
    }

    private static string GetSavesDirectory(GameInstance instance)
    {
        return Path.Combine(instance.InstanceDirectory, "saves");
    }
}
