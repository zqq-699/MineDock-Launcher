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

using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Launcher.Infrastructure.FileSystem;

internal sealed class LocalSaveArchiveImporter
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

    private readonly ILogger logger;

    public LocalSaveArchiveImporter(ILogger logger)
    {
        this.logger = logger;
    }

    public LocalSaveImportResult Import(
        string instanceId,
        string savesDirectory,
        string archivePath,
        Func<string, LocalSave> createSave,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
        {
            logger.LogInformation(
                "Skipping local save import because archive does not exist. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instanceId,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.FileNotFound);
        }

        if (!IsSupportedArchivePath(normalizedArchivePath))
        {
            logger.LogInformation(
                "Skipping local save import because archive type is unsupported. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instanceId,
                normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnsupportedArchive);
        }

        logger.LogInformation(
            "Importing local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
            instanceId,
            normalizedArchivePath);

        try
        {
            return ImportValidatedArchive(
                instanceId,
                savesDirectory,
                normalizedArchivePath,
                createSave,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Failed to open local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}", instanceId, normalizedArchivePath);
            return UnexpectedFailure();
        }
        catch (InvalidFormatException exception)
        {
            logger.LogWarning(exception, "Failed to parse local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}", instanceId, normalizedArchivePath);
            return UnexpectedFailure();
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(exception, "Unsupported local save archive feature encountered. InstanceId={InstanceId} ArchivePath={ArchivePath}", instanceId, normalizedArchivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnsupportedArchive);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Failed to import local save archive because a file operation failed. InstanceId={InstanceId} ArchivePath={ArchivePath}", instanceId, normalizedArchivePath);
            return UnexpectedFailure();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Failed to import local save archive because access was denied. InstanceId={InstanceId} ArchivePath={ArchivePath}", instanceId, normalizedArchivePath);
            return UnexpectedFailure();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected failure while importing local save archive. InstanceId={InstanceId} ArchivePath={ArchivePath}", instanceId, normalizedArchivePath);
            return UnexpectedFailure();
        }
    }

    private LocalSaveImportResult ImportValidatedArchive(
        string instanceId,
        string savesDirectory,
        string archivePath,
        Func<string, LocalSave> createSave,
        CancellationToken cancellationToken)
    {
        var entries = ReadArchiveEntries(archivePath);
        if (entries.Length == 0)
        {
            logger.LogInformation(
                "Local save archive did not contain any usable entries. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instanceId,
                archivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.InvalidMinecraftSaveArchive);
        }

        var archiveRoot = ResolveSaveArchiveRoot(entries);
        if (archiveRoot is null)
        {
            logger.LogInformation(
                "Local save archive is not a valid Minecraft save. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instanceId,
                archivePath);
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.InvalidMinecraftSaveArchive);
        }

        Directory.CreateDirectory(savesDirectory);
        var preferredDirectoryName = archiveRoot.Length == 0 ? RemoveAllExtensions(archivePath) : archiveRoot;
        var resolvedDirectoryName = ResolveUniqueDirectoryName(
            savesDirectory,
            SanitizeDirectoryName(preferredDirectoryName));
        var targetSaveDirectory = Path.Combine(savesDirectory, resolvedDirectoryName);
        logger.LogInformation(
            "Resolved local save archive target. InstanceId={InstanceId} ArchivePath={ArchivePath} SaveDirectory={SaveDirectory}",
            instanceId,
            archivePath,
            targetSaveDirectory);

        Directory.CreateDirectory(targetSaveDirectory);
        try
        {
            ExtractArchiveEntries(archivePath, archiveRoot, targetSaveDirectory, cancellationToken);
        }
        catch
        {
            SafeDeleteDirectory(targetSaveDirectory);
            throw;
        }

        var importedSave = createSave(targetSaveDirectory);
        logger.LogInformation(
            "Local save archive imported. InstanceId={InstanceId} ArchivePath={ArchivePath} SaveDirectory={SaveDirectory}",
            instanceId,
            archivePath,
            targetSaveDirectory);
        return LocalSaveImportResult.Success(importedSave);
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
            if (!ShouldIgnoreArchiveEntry(normalizedPath))
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
            var relativePath = ResolveRelativeEntryPath(entry.Key, archiveRoot);
            if (relativePath.Length == 0)
                continue;

            using var entryStream = entry.OpenEntryStream();
            ExtractEntry(entryStream, targetSaveDirectory, relativePath, cancellationToken);
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

            var relativePath = ResolveRelativeEntryPath(entry.Name, archiveRoot);
            if (relativePath.Length != 0)
                ExtractEntry(entry.DataStream, targetSaveDirectory, relativePath, cancellationToken);
        }
    }

    private static string ResolveRelativeEntryPath(string? entryPath, string archiveRoot)
    {
        var normalizedPath = NormalizeArchivePath(entryPath);
        return ShouldIgnoreArchiveEntry(normalizedPath)
            ? string.Empty
            : GetRelativeEntryPath(normalizedPath, archiveRoot);
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
        return entries.Any(entry => string.Equals(
            entry.NormalizedPath,
            rootDirectory + "/level.dat",
            StringComparison.OrdinalIgnoreCase))
            ? rootDirectory
            : null;
    }

    private static string GetRelativeEntryPath(string normalizedEntryPath, string archiveRoot)
    {
        if (archiveRoot.Length == 0)
            return normalizedEntryPath;

        return normalizedEntryPath.StartsWith(archiveRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? normalizedEntryPath[(archiveRoot.Length + 1)..]
            : string.Empty;
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

        using var target = new FileStream(fullTargetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = sourceStream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
                break;

            target.Write(buffer, 0, bytesRead);
        }
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
        var separatorIndex = normalizedPath.IndexOf('/');
        return separatorIndex < 0 ? null : normalizedPath[..separatorIndex];
    }

    private static string ResolveUniqueDirectoryName(string parentDirectory, string preferredName)
    {
        var candidate = preferredName;
        var index = 1;
        while (Directory.Exists(Path.Combine(parentDirectory, candidate)))
            candidate = $"{preferredName} ({index++})";

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
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static LocalSaveImportResult UnexpectedFailure()
    {
        return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError);
    }

    private sealed record ArchiveEntryDescriptor(string NormalizedPath);
}
