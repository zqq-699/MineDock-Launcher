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

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Security.Cryptography;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalSaveService : ILocalSaveService
{
    private const string DefaultImportedSaveDirectoryName = "Imported Save";
    private static readonly string[] SupportedArchiveExtensions =
    [
        ".zip",
        ".7z",
        ".rar",
        ".tar",
        ".gz",
        ".tgz",
        ".bz2",
        ".tar.gz",
        ".tar.bz2",
        ".tbz2"
    ];

    private readonly LauncherPathProvider pathProvider;
    private readonly ILogger<LocalSaveService> logger;
    private readonly string iconCacheDirectory;

    public LocalSaveService(LauncherPathProvider? pathProvider = null, ILogger<LocalSaveService>? logger = null)
    {
        this.pathProvider = pathProvider ?? new LauncherPathProvider();
        this.logger = logger ?? NullLogger<LocalSaveService>.Instance;
        iconCacheDirectory = Path.Combine(this.pathProvider.DefaultDataDirectory, "cache", "saves", "icons");
    }

    public Task<IReadOnlyList<LocalSave>> GetSavesAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Task.Run<IReadOnlyList<LocalSave>>(
            () =>
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

                logger.LogInformation(
                    "Local saves loaded. InstanceId={InstanceId} Count={SaveCount}",
                    instance.Id,
                    saves.Length);
                return saves;
            },
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
            () => ImportFromArchiveCore(instance, archivePath, cancellationToken),
            cancellationToken);
    }

    public Task DeleteAsync(LocalSave save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(save.FullPath))
                {
                    logger.LogInformation(
                        "Skipping local save delete because directory does not exist. Path={Path}",
                        save.FullPath);
                    return;
                }

                Directory.Delete(save.FullPath, recursive: true);
                logger.LogInformation("Local save deleted. Path={Path}", save.FullPath);
            },
            cancellationToken);
    }

    public Task DeleteAsync(IEnumerable<LocalSave> saves, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saves);

        return Task.Run(
            async () =>
            {
                foreach (var save in saves.DistinctBy(save => save.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DeleteAsync(save, cancellationToken);
                }
            },
            cancellationToken);
    }

    private LocalSaveImportResult ImportFromArchiveCore(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
        {
            logger.LogInformation(
                "Skipping local save import because archive does not exist. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.FileNotFound);
        }

        if (!IsSupportedArchivePath(normalizedArchivePath))
        {
            logger.LogInformation(
                "Skipping local save import because archive type is unsupported. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnsupportedArchive);
        }

        logger.LogInformation(
            "Importing local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
            instance.Id,
            normalizedArchivePath);

        try
        {
            var entries = ReadArchiveEntries(normalizedArchivePath);

            if (entries.Length == 0)
            {
                logger.LogInformation(
                    "Local save archive did not contain any usable entries. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                    instance.Id,
                    normalizedArchivePath);
                return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.InvalidMinecraftSaveArchive);
            }

            var archiveRoot = ResolveSaveArchiveRoot(entries);
            if (archiveRoot is null)
            {
                logger.LogInformation(
                    "Local save archive is not a valid Minecraft save. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                    instance.Id,
                    normalizedArchivePath);
                return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.InvalidMinecraftSaveArchive);
            }

            var savesDirectory = GetSavesDirectory(instance);
            Directory.CreateDirectory(savesDirectory);

            var preferredDirectoryName = archiveRoot.Length == 0
                ? RemoveAllExtensions(normalizedArchivePath)
                : archiveRoot;
            var resolvedDirectoryName = ResolveUniqueDirectoryName(
                savesDirectory,
                SanitizeDirectoryName(preferredDirectoryName));
            var targetSaveDirectory = Path.Combine(savesDirectory, resolvedDirectoryName);

            logger.LogInformation(
                "Resolved local save archive target. InstanceId={InstanceId} ArchivePath={ArchivePath} SaveDirectory={SaveDirectory}",
                instance.Id,
                normalizedArchivePath,
                targetSaveDirectory);

            Directory.CreateDirectory(targetSaveDirectory);

            try
            {
                ExtractArchiveEntries(normalizedArchivePath, archiveRoot, targetSaveDirectory, cancellationToken);
            }
            catch
            {
                SafeDeleteDirectory(targetSaveDirectory);
                throw;
            }

            var importedSave = ToLocalSave(targetSaveDirectory);
            logger.LogInformation(
                "Local save archive imported. InstanceId={InstanceId} ArchivePath={ArchivePath} SaveDirectory={SaveDirectory}",
                instance.Id,
                normalizedArchivePath,
                targetSaveDirectory);
            return LocalSaveImportResult.Success(importedSave);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to open local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError);
        }
        catch (InvalidFormatException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to parse local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError);
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(
                exception,
                "Unsupported local save archive feature encountered. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnsupportedArchive);
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local save archive because a file operation failed. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local save archive because access was denied. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected failure while importing local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError);
        }
    }

    private static ArchiveEntryDescriptor[] ReadArchiveEntries(string archivePath)
    {
        return IsTarArchivePath(archivePath)
            ? ReadTarArchiveEntries(archivePath)
            : ReadGenericArchiveEntries(archivePath);
    }

    private static ArchiveEntryDescriptor[] ReadGenericArchiveEntries(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.Open(stream);
        return archive.Entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => NormalizeArchivePath(entry.Key))
            .Where(normalizedPath => !ShouldIgnoreArchiveEntry(normalizedPath))
            .Select(normalizedPath => new ArchiveEntryDescriptor(normalizedPath))
            .ToArray();
    }

    private static ArchiveEntryDescriptor[] ReadTarArchiveEntries(string archivePath)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var archiveStream = OpenTarArchiveStream(fileStream, archivePath);
        using var reader = new TarReader(archiveStream, leaveOpen: false);

        var entries = new List<ArchiveEntryDescriptor>();
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            if (entry.EntryType is TarEntryType.Directory || entry.DataStream is null)
                continue;

            var normalizedPath = NormalizeArchivePath(entry.Name);
            if (ShouldIgnoreArchiveEntry(normalizedPath))
                continue;

            entries.Add(new ArchiveEntryDescriptor(normalizedPath));
        }

        return [.. entries];
    }

    private static void ExtractArchiveEntries(
        string archivePath,
        string archiveRoot,
        string targetSaveDirectory,
        CancellationToken cancellationToken)
    {
        if (IsTarArchivePath(archivePath))
        {
            ExtractTarArchiveEntries(archivePath, archiveRoot, targetSaveDirectory, cancellationToken);
            return;
        }

        ExtractGenericArchiveEntries(archivePath, archiveRoot, targetSaveDirectory, cancellationToken);
    }

    private static void ExtractGenericArchiveEntries(
        string archivePath,
        string archiveRoot,
        string targetSaveDirectory,
        CancellationToken cancellationToken)
    {
        using var extractionStream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.Open(extractionStream);
        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedEntryPath = NormalizeArchivePath(entry.Key);
            if (ShouldIgnoreArchiveEntry(normalizedEntryPath))
                continue;

            var relativePath = GetRelativeEntryPath(normalizedEntryPath, archiveRoot);
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            ExtractEntry(entry.OpenEntryStream(), targetSaveDirectory, relativePath, cancellationToken);
        }
    }

    private static void ExtractTarArchiveEntries(
        string archivePath,
        string archiveRoot,
        string targetSaveDirectory,
        CancellationToken cancellationToken)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var archiveStream = OpenTarArchiveStream(fileStream, archivePath);
        using var reader = new TarReader(archiveStream, leaveOpen: false);

        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.EntryType is TarEntryType.Directory || entry.DataStream is null)
                continue;

            var normalizedEntryPath = NormalizeArchivePath(entry.Name);
            if (ShouldIgnoreArchiveEntry(normalizedEntryPath))
                continue;

            var relativePath = GetRelativeEntryPath(normalizedEntryPath, archiveRoot);
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            ExtractEntry(entry.DataStream, targetSaveDirectory, relativePath, cancellationToken);
        }
    }

    private static Stream OpenTarArchiveStream(Stream sourceStream, string archivePath)
    {
        return archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
               || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(sourceStream, CompressionMode.Decompress)
            : sourceStream;
    }

    private static bool IsTarArchivePath(string archivePath)
    {
        return archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
            || archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSaveArchiveRoot(IReadOnlyList<ArchiveEntryDescriptor> entries)
    {
        if (entries.Any(entry => string.Equals(entry.NormalizedPath, "level.dat", StringComparison.OrdinalIgnoreCase)))
            return string.Empty;

        var topLevelDirectories = entries
            .Select(entry => GetTopLevelSegment(entry.NormalizedPath))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (topLevelDirectories.Length != 1)
            return null;

        var rootDirectory = topLevelDirectories[0];
        return entries.Any(entry => string.Equals(entry.NormalizedPath, rootDirectory + "/level.dat", StringComparison.OrdinalIgnoreCase))
            ? rootDirectory
            : null;
    }

    private static string GetRelativeEntryPath(string normalizedEntryPath, string archiveRoot)
    {
        if (archiveRoot.Length == 0)
            return normalizedEntryPath;

        if (!normalizedEntryPath.StartsWith(archiveRoot + "/", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return normalizedEntryPath[(archiveRoot.Length + 1)..];
    }

    private static void ExtractEntry(
        Stream sourceStream,
        string targetDirectory,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var relativePathForDisk = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullTargetPath = GetValidatedTargetPath(targetDirectory, relativePathForDisk);
        var targetParentDirectory = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrWhiteSpace(targetParentDirectory))
            Directory.CreateDirectory(targetParentDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        using var target = new FileStream(fullTargetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        sourceStream.CopyTo(target);
    }

    private static string GetValidatedTargetPath(string targetDirectory, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(targetDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var comparisonRoot = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(comparisonRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Archive entry resolved outside the target directory.");
        }

        return fullPath;
    }

    private static bool ShouldIgnoreArchiveEntry(string normalizedPath)
    {
        return normalizedPath.Length == 0
            || normalizedPath.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, ".ds_store", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("/.ds_store", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArchivePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        normalized = normalized.TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("/", segments);
    }

    private static string? GetTopLevelSegment(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return null;

        var separatorIndex = normalizedPath.IndexOf('/');
        if (separatorIndex < 0)
            return null;

        return normalizedPath[..separatorIndex];
    }

    private static string ResolveUniqueDirectoryName(string parentDirectory, string preferredName)
    {
        var candidate = preferredName;
        var index = 1;

        while (Directory.Exists(Path.Combine(parentDirectory, candidate)))
        {
            candidate = $"{preferredName} ({index})";
            index++;
        }

        return candidate;
    }

    private static string SanitizeDirectoryName(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
            return DefaultImportedSaveDirectoryName;

        var sanitizedCharacters = directoryName
            .Trim()
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
            .ToArray();
        var sanitized = new string(sanitizedCharacters).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? DefaultImportedSaveDirectoryName : sanitized;
    }

    private static string RemoveAllExtensions(string archivePath)
    {
        var fileName = Path.GetFileName(archivePath);
        while (true)
        {
            var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (string.Equals(fileName, withoutExtension, StringComparison.Ordinal))
                return withoutExtension;

            fileName = withoutExtension;
        }
    }

    private static bool IsSupportedArchivePath(string archivePath)
    {
        return SupportedArchiveExtensions.Any(extension =>
            archivePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        Directory.Delete(path, recursive: true);
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

    private sealed record ArchiveEntryDescriptor(string NormalizedPath);
}
