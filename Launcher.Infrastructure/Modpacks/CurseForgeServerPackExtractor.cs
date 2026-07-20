/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class CurseForgeServerPackExtractor : IServerPackExtractor
{
    private const long MaxEntryBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxTotalBytes = 16L * 1024 * 1024 * 1024;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;

    public async Task ExtractAsync(
        string archivePath,
        string targetDirectory,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress(ImportProgressStages.PreparingArchive, string.Empty));
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long reservedBytes = 0;
        var files = archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)).ToArray();
        var completed = 0;
        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntry(entry);
            reservedBytes = checked(reservedBytes + entry.Length);
            if (reservedBytes > MaxTotalBytes)
                throw InvalidArchive("Server pack contents exceed the allowed total size.");

            var relativePath = ValidateRelativePath(entry.FullName);
            var targetPath = ModpackArchiveUtility.GetValidatedTargetPath(targetDirectory, relativePath);
            MinecraftPathGuard.EnsureSafeFileDestination(targetPath, targetDirectory, "Server pack entry");
            if (!targets.Add(Path.GetFullPath(targetPath)))
                throw InvalidArchive($"Multiple server pack entries resolve to the same path: {relativePath}");
            var parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            await using var source = entry.Open();
            await using var destination = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyEntryAsync(source, destination, entry.Length, cancellationToken).ConfigureAwait(false);
            completed++;
            progress?.Report(new LauncherProgress(
                ImportProgressStages.CopyingOverrides,
                Path.GetFileName(relativePath),
                files.Length == 0 ? 100 : completed * 100d / files.Length));
        }

        FlattenSingleTopLevelDirectory(targetDirectory);
    }

    private static void ValidateEntry(ZipArchiveEntry entry)
    {
        if (entry.Length < 0 || entry.Length > MaxEntryBytes)
            throw InvalidArchive($"Server pack entry is too large: {entry.FullName}");
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        if (unixMode == UnixSymbolicLink)
            throw InvalidArchive($"Server pack symbolic links are not allowed: {entry.FullName}");
    }

    private static string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith('/')
            || path.StartsWith('\\')
            || Path.IsPathFullyQualified(path))
        {
            throw InvalidArchive($"Server pack contains an invalid path: {path}");
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".."
                || segment.Contains(':')
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw InvalidArchive($"Server pack contains an invalid path: {path}");
        }
        return string.Join('/', segments);
    }

    private static async Task CopyEntryAsync(
        Stream source,
        Stream destination,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            copied += read;
            if (copied > MaxEntryBytes || copied > expectedLength)
                throw InvalidArchive("Server pack entry expanded beyond its declared size.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (copied != expectedLength)
            throw InvalidArchive("Server pack entry length does not match its archive metadata.");
    }

    private static void FlattenSingleTopLevelDirectory(string targetDirectory)
    {
        var directories = Directory.EnumerateDirectories(targetDirectory)
            .Where(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            .ToArray();
        if (directories.Length != 1)
            return;

        var nested = directories[0];
        foreach (var path in Directory.EnumerateFileSystemEntries(nested).ToArray())
        {
            var destination = Path.Combine(targetDirectory, Path.GetFileName(path));
            if (File.Exists(destination) || Directory.Exists(destination))
                throw InvalidArchive($"Flattening the server pack would overwrite: {Path.GetFileName(path)}");
            if (Directory.Exists(path))
                Directory.Move(path, destination);
            else
                File.Move(path, destination);
        }
        Directory.Delete(nested);
    }

    private static ModpackImportException InvalidArchive(string message) =>
        new(ModpackImportFailureReason.InvalidManifest, message);
}
