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

using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Launcher.Infrastructure.FileSystem;

/// <summary>
/// 通过临时归档和暂存目录创建、删除及恢复实例备份，避免失败时破坏现有实例。
/// </summary>
public sealed class InstanceBackupService : IInstanceBackupService
{
    private const string ManifestFileName = "launcher-backups.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<InstanceBackupService> logger;

    public InstanceBackupService(ILogger<InstanceBackupService>? logger = null)
    {
        this.logger = logger ?? NullLogger<InstanceBackupService>.Instance;
    }

    public Task<string> EnsureBackupDirectoryAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedDirectory = Path.GetFullPath(backupDirectory);
                Directory.CreateDirectory(normalizedDirectory);
                logger.LogInformation("Instance backup directory ensured. BackupDirectory={BackupDirectory}", normalizedDirectory);
                return normalizedDirectory;
            },
            cancellationToken);
    }

    public Task<int> CountBackupEntriesAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
            return Task.FromResult(0);

        return CountBackupEntriesCoreAsync(backupDirectory, cancellationToken);
    }

    public Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
            return Task.FromResult<IReadOnlyList<InstanceBackupRecord>>([]);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var normalizedDirectory = Path.GetFullPath(backupDirectory);
                    if (!Directory.Exists(normalizedDirectory))
                        return (IReadOnlyList<InstanceBackupRecord>)[];

                    var directoryLock = GetDirectoryLock(normalizedDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    List<InstanceBackupRecord> filteredRecords;
                    try
                    {
                        var manifestPath = GetManifestPath(normalizedDirectory);
                        var records = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                        filteredRecords = NormalizeAndFilterRecords(normalizedDirectory, records);
                        if (filteredRecords.Count != records.Count)
                            await WriteManifestAsync(manifestPath, filteredRecords, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        directoryLock.Release();
                    }

                    logger.LogInformation(
                        "Instance backups loaded. BackupDirectory={BackupDirectory} Count={BackupCount}",
                        normalizedDirectory,
                        filteredRecords.Count);
                    return (IReadOnlyList<InstanceBackupRecord>)filteredRecords;
                }
                catch (Exception exception) when (exception is IOException
                                                 or UnauthorizedAccessException
                                                 or ArgumentException
                                                 or NotSupportedException
                                                 or JsonException)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to load instance backups. BackupDirectory={BackupDirectory}",
                        backupDirectory);
                    return (IReadOnlyList<InstanceBackupRecord>)[];
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// 在备份目录生成临时 ZIP，原子提交后更新清单；失败时删除所有半成品。
    /// </summary>
    public Task<InstanceBackupRecord> CreateBackupAsync(
        GameInstance instance,
        string backupDirectory,
        string backupName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupName);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedBackupDirectory = Path.GetFullPath(backupDirectory);
                var normalizedInstanceDirectory = Path.GetFullPath(instance.InstanceDirectory);

                logger.LogInformation(
                    "Creating instance backup. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupDirectory={BackupDirectory}",
                    instance.Id,
                    normalizedInstanceDirectory,
                    normalizedBackupDirectory);

                if (!Directory.Exists(normalizedInstanceDirectory))
                {
                    throw new InstanceBackupException(
                        InstanceBackupFailureReason.InstanceDirectoryNotFound,
                        "Instance directory does not exist.");
                }

                if (IsSameOrChildPath(normalizedInstanceDirectory, normalizedBackupDirectory))
                {
                    throw new InstanceBackupException(
                        InstanceBackupFailureReason.BackupDirectoryInsideInstance,
                        "Backup directory cannot be inside the instance directory.");
                }

                Directory.CreateDirectory(normalizedBackupDirectory);
                var sanitizedBackupName = NormalizeBackupName(backupName);
                var tempPath = Path.Combine(normalizedBackupDirectory, $".launcher-backup.{Guid.NewGuid():N}.tmp");
                string? finalPath = null;
                var shouldRollbackFinalPath = false;

                try
                {
                    CreateInstanceZip(normalizedInstanceDirectory, instance.Name, tempPath, cancellationToken);
                    var directoryLock = GetDirectoryLock(normalizedBackupDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var manifestPath = GetManifestPath(normalizedBackupDirectory);
                        var records = NormalizeAndFilterRecords(
                            normalizedBackupDirectory,
                            await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false));
                        var fileName = ResolveUniqueBackupFileName(normalizedBackupDirectory, records, sanitizedBackupName);
                        finalPath = Path.Combine(normalizedBackupDirectory, fileName);

                        File.Move(tempPath, finalPath);
                        shouldRollbackFinalPath = true;

                        var record = new InstanceBackupRecord
                        {
                            Name = backupName.Trim(),
                            FileName = fileName,
                            FullPath = finalPath,
                            SizeBytes = new FileInfo(finalPath).Length,
                            CreatedAt = DateTimeOffset.UtcNow
                        };

                        records.Add(record);
                        await WriteManifestAsync(manifestPath, records, cancellationToken).ConfigureAwait(false);
                        shouldRollbackFinalPath = false;

                        logger.LogInformation(
                            "Instance backup created. InstanceId={InstanceId} BackupFile={BackupFile}",
                            instance.Id,
                            finalPath);
                        return record;
                    }
                    catch
                    {
                        if (shouldRollbackFinalPath && finalPath is not null)
                            TryDeleteFileIfExists(finalPath);
                        throw;
                    }
                    finally
                    {
                        directoryLock.Release();
                    }
                }
                catch (Exception exception) when (exception is not InstanceBackupException)
                {
                    TryDeleteFileIfExists(tempPath);
                    logger.LogError(
                        exception,
                        "Failed to create instance backup. InstanceId={InstanceId} BackupDirectory={BackupDirectory}",
                        instance.Id,
                        normalizedBackupDirectory);
                    throw;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// 仅允许删除备份根目录内的文件，并同步移除清单记录。
    /// </summary>
    public Task DeleteBackupAsync(
        string backupDirectory,
        string backupFullPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFullPath);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedBackupDirectory = Path.GetFullPath(backupDirectory);
                var normalizedBackupFullPath = Path.GetFullPath(backupFullPath);
                if (!IsSameOrChildPath(normalizedBackupDirectory, normalizedBackupFullPath))
                    throw new ArgumentException("Backup file must be inside the backup directory.", nameof(backupFullPath));

                logger.LogInformation(
                    "Deleting instance backup. BackupDirectory={BackupDirectory} BackupFile={BackupFile}",
                    normalizedBackupDirectory,
                    normalizedBackupFullPath);

                try
                {
                    var directoryLock = GetDirectoryLock(normalizedBackupDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var manifestPath = GetManifestPath(normalizedBackupDirectory);
                        if (File.Exists(normalizedBackupFullPath))
                            File.Delete(normalizedBackupFullPath);

                        var records = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                        var remainingRecords = NormalizeAndFilterRecords(normalizedBackupDirectory, records)
                            .Where(record => !string.Equals(record.FullPath, normalizedBackupFullPath, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        await WriteManifestAsync(manifestPath, remainingRecords, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        directoryLock.Release();
                    }

                    logger.LogInformation(
                        "Instance backup deleted. BackupDirectory={BackupDirectory} BackupFile={BackupFile}",
                        normalizedBackupDirectory,
                        normalizedBackupFullPath);
                }
                catch (Exception exception) when (exception is IOException
                                                 or UnauthorizedAccessException
                                                 or ArgumentException
                                                 or NotSupportedException
                                                 or JsonException)
                {
                    logger.LogError(
                        exception,
                        "Failed to delete instance backup. BackupDirectory={BackupDirectory} BackupFile={BackupFile}",
                        normalizedBackupDirectory,
                        normalizedBackupFullPath);
                    throw;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// 在同级暂存目录解压并交换实例目录，交换失败时恢复原实例。
    /// </summary>
    public Task RestoreBackupAsync(
        GameInstance instance,
        string backupDirectory,
        string backupFullPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFullPath);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedBackupDirectory = Path.GetFullPath(backupDirectory);
                var normalizedBackupFullPath = Path.GetFullPath(backupFullPath);
                var normalizedInstanceDirectory = Path.GetFullPath(instance.InstanceDirectory);
                var minecraftDirectory = GetMinecraftDirectory(normalizedInstanceDirectory);
                if (!IsSameOrChildPath(normalizedBackupDirectory, normalizedBackupFullPath))
                    throw new ArgumentException("Backup file must be inside the backup directory.", nameof(backupFullPath));

                if (IsSameOrChildPath(normalizedInstanceDirectory, normalizedBackupDirectory))
                {
                    throw new InstanceBackupException(
                        InstanceBackupFailureReason.BackupDirectoryInsideInstance,
                        "Backup directory cannot be inside the instance directory.");
                }

                var instanceParentDirectory = Directory.GetParent(normalizedInstanceDirectory)!.FullName;
                Directory.CreateDirectory(instanceParentDirectory);

                // 在实例同级目录完成解压和目录交换，确保移动可回滚且不跨卷复制。
                var stagingDirectory = Path.Combine(instanceParentDirectory, $".launcher-restore-{Guid.NewGuid():N}");
                var extractDirectory = Path.Combine(stagingDirectory, "extract");
                var previousDirectory = Path.Combine(stagingDirectory, "previous");
                var movedPreviousDirectory = false;
                string archiveRootName;

                try
                {
                    var directoryLock = GetDirectoryLock(normalizedBackupDirectory);
                    await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (!File.Exists(normalizedBackupFullPath))
                            throw new FileNotFoundException("Backup file does not exist.", normalizedBackupFullPath);

                        logger.LogInformation(
                            "Restoring instance backup. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupFile={BackupFile}",
                            instance.Id,
                            normalizedInstanceDirectory,
                            normalizedBackupFullPath);

                        archiveRootName = GetArchiveRootDirectoryName(normalizedBackupFullPath);
                        Directory.CreateDirectory(extractDirectory);
                        ZipFile.ExtractToDirectory(normalizedBackupFullPath, extractDirectory);
                    }
                    finally
                    {
                        directoryLock.Release();
                    }
                    cancellationToken.ThrowIfCancellationRequested();

                    var restoredDirectory = Path.GetFullPath(Path.Combine(extractDirectory, archiveRootName));
                    if (!IsSameOrChildPath(extractDirectory, restoredDirectory) || !Directory.Exists(restoredDirectory))
                        throw new InvalidDataException("Backup archive does not contain a valid instance root directory.");

                    await using (var mutationLock = await CrossProcessVersionLock.AcquireAsync(
                                     CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
                                     progress: null,
                                     cancellationToken).ConfigureAwait(false))
                    {
                        await EnsureCurrentInstanceIdentityAsync(
                                normalizedInstanceDirectory,
                                instance.Id,
                                cancellationToken)
                            .ConfigureAwait(false);

                        Directory.Move(normalizedInstanceDirectory, previousDirectory);
                        movedPreviousDirectory = true;

                        try
                        {
                            Directory.Move(restoredDirectory, normalizedInstanceDirectory);
                        }
                        catch
                        {
                            if (Directory.Exists(previousDirectory)
                                && !Directory.Exists(normalizedInstanceDirectory))
                            {
                                Directory.Move(previousDirectory, normalizedInstanceDirectory);
                                movedPreviousDirectory = false;
                            }

                            throw;
                        }
                    }

                    if (Directory.Exists(previousDirectory))
                    {
                        Directory.Delete(previousDirectory, recursive: true);
                        movedPreviousDirectory = false;
                    }

                    logger.LogInformation(
                        "Instance backup restored. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupFile={BackupFile}",
                        instance.Id,
                        normalizedInstanceDirectory,
                        normalizedBackupFullPath);
                }
                catch (Exception exception) when (exception is IOException
                                                 or UnauthorizedAccessException
                                                 or ArgumentException
                                                 or InvalidDataException
                                                 or InvalidOperationException
                                                 or NotSupportedException)
                {
                    if (movedPreviousDirectory
                        && Directory.Exists(previousDirectory)
                        && !Directory.Exists(normalizedInstanceDirectory))
                    {
                        // 只有新目录尚未就位时才恢复旧目录，避免覆盖已经成功替换的实例。
                        Directory.Move(previousDirectory, normalizedInstanceDirectory);
                    }

                    logger.LogError(
                        exception,
                        "Failed to restore instance backup. InstanceId={InstanceId} InstanceDirectory={InstanceDirectory} BackupFile={BackupFile}",
                        instance.Id,
                        normalizedInstanceDirectory,
                        normalizedBackupFullPath);
                    throw;
                }
                finally
                {
                    DeleteDirectoryIfExists(stagingDirectory);
                }
            },
            cancellationToken);
    }

    private static string GetMinecraftDirectory(string normalizedInstanceDirectory)
    {
        var versionsDirectory = Directory.GetParent(normalizedInstanceDirectory)?.FullName;
        if (versionsDirectory is null
            || !string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(versionsDirectory)),
                "versions",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Instance directory must be a direct child of the Minecraft versions directory.");
        }

        return Directory.GetParent(versionsDirectory)?.FullName
            ?? throw new InvalidOperationException("Minecraft directory could not be determined from the instance path.");
    }

    private static async Task EnsureCurrentInstanceIdentityAsync(
        string instanceDirectory,
        string expectedInstanceId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(instanceDirectory))
        {
            throw new InstanceBackupException(
                InstanceBackupFailureReason.InstanceChanged,
                "The instance was moved or deleted while its backup was being restored.");
        }

        var settingsPath = Path.Combine(
            instanceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            "instance-settings.json");
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("Id", out var idElement)
                || !string.Equals(idElement.GetString(), expectedInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstanceBackupException(
                    InstanceBackupFailureReason.InstanceChanged,
                    "The instance directory now belongs to a different instance.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InstanceBackupException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            throw new InstanceBackupException(
                InstanceBackupFailureReason.InstanceChanged,
                "The current instance identity could not be verified.",
                exception);
        }
    }

    private async Task<int> CountBackupEntriesCoreAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var backups = await GetBackupsAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
            return backups.Count;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or ArgumentException
                                         or NotSupportedException
                                         or JsonException)
        {
            logger.LogWarning(
                exception,
                "Failed to count instance backups. BackupDirectory={BackupDirectory}",
                backupDirectory);
            return 0;
        }
    }

    private static string GetManifestPath(string backupDirectory)
    {
        return Path.Combine(backupDirectory, ManifestFileName);
    }

    private static async Task<List<InstanceBackupRecord>> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
            return [];

        await using var stream = File.OpenRead(manifestPath);
        var records = await JsonSerializer.DeserializeAsync<List<InstanceBackupRecord>>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        return records ?? [];
    }

    private static async Task WriteManifestAsync(
        string manifestPath,
        IReadOnlyList<InstanceBackupRecord> records,
        CancellationToken cancellationToken)
    {
        await AtomicJsonFileWriter.WriteAsync(manifestPath, records, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static SemaphoreSlim GetDirectoryLock(string normalizedBackupDirectory)
    {
        var lockKey = Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalizedBackupDirectory));
        return DirectoryLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// 规范化清单路径并剔除越界、重复或实际文件已缺失的记录。
    /// </summary>
    private static List<InstanceBackupRecord> NormalizeAndFilterRecords(
        string backupDirectory,
        IEnumerable<InstanceBackupRecord> records)
    {
        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.FileName))
            .Select(record =>
            {
                var fileName = Path.GetFileName(record.FileName);
                var fullPath = string.IsNullOrWhiteSpace(record.FullPath)
                    ? Path.Combine(backupDirectory, fileName)
                    : Path.GetFullPath(record.FullPath);
                var fileInfo = new FileInfo(fullPath);
                return new InstanceBackupRecord
                {
                    Name = string.IsNullOrWhiteSpace(record.Name) ? Path.GetFileNameWithoutExtension(fileName) : record.Name,
                    FileName = fileName,
                    FullPath = fullPath,
                    SizeBytes = fileInfo.Exists ? fileInfo.Length : record.SizeBytes,
                    CreatedAt = record.CreatedAt
                };
            })
            .Where(record => File.Exists(record.FullPath))
            .OrderByDescending(record => record.CreatedAt)
            .ThenBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ResolveUniqueBackupFileName(
        string backupDirectory,
        IReadOnlyList<InstanceBackupRecord> records,
        string backupName)
    {
        var usedNames = new HashSet<string>(
            records.Select(record => record.FileName),
            StringComparer.OrdinalIgnoreCase);
        var baseName = backupName;
        var candidate = $"{baseName}.zip";
        var index = 1;
        while (usedNames.Contains(candidate) || File.Exists(Path.Combine(backupDirectory, candidate)))
        {
            candidate = $"{baseName} ({index}).zip";
            index++;
        }

        return candidate;
    }

    private static string NormalizeBackupName(string backupName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(backupName.Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Backup" : sanitized;
    }

    /// <summary>
    /// 将实例目录写入带单一根目录的 ZIP，并逐文件检查取消。
    /// </summary>
    private static void CreateInstanceZip(
        string instanceDirectory,
        string instanceName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var rootName = Path.GetFileName(instanceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
            rootName = NormalizeBackupName(instanceName);

        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        foreach (var filePath in Directory.EnumerateFiles(instanceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(instanceDirectory, filePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var entryPath = $"{rootName}/{relativePath}";
            archive.CreateEntryFromFile(filePath, entryPath, CompressionLevel.Optimal);
        }
    }

    private static string GetArchiveRootDirectoryName(string backupFullPath)
    {
        using var archive = ZipFile.OpenRead(backupFullPath);
        var rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var normalizedEntryName = entry.FullName.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalizedEntryName))
                continue;

            var separatorIndex = normalizedEntryName.IndexOf('/');
            if (separatorIndex <= 0)
                throw new InvalidDataException("Backup archive must contain the instance root directory.");

            var rootName = normalizedEntryName[..separatorIndex];
            if (rootName is "." or ".." || rootName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Backup archive contains an invalid instance root directory.");

            rootNames.Add(rootName);
        }

        return rootNames.Count == 1
            ? rootNames.Single()
            : throw new InvalidDataException("Backup archive must contain exactly one instance root directory.");
    }

    private static bool IsSameOrChildPath(string parentDirectory, string candidateDirectory)
    {
        var normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parentDirectory));
        var normalizedCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidateDirectory));
        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void TryDeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
