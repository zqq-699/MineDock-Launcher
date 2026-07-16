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

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Repositories;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

public sealed partial class GameInstanceService
{
private const string ResourceProjectIconFileName = "resource-project-icon.png";

public async Task<GameInstance> CreateInstanceAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string? name,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0,
        bool installFabricApi = true,
        string? fabricApiVersionId = null,
        string? quiltStandardLibraryVersionId = null)
    {
        return await creationCoordinator.CreateAsync(
                minecraftVersion,
                loader,
                loaderVersion,
                name,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                installFabricApi,
                fabricApiVersionId,
                quiltStandardLibraryVersionId)
            .ConfigureAwait(false);
    }

    public async Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = (await GetInstancesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var index = instances.FindIndex(existing => existing.Id == instance.Id);
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        if (index >= 0)
            instances[index] = instance;
        else
            instances.Add(instance);

        await repository.UpdateInstanceAsync(settings.MinecraftDirectory, instance, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Game instance saved. InstanceId={InstanceId} VersionName={VersionName}", instance.Id, instance.VersionName);
    }

    /// <summary>
    /// 校验实例新名称，事务性重命名版本目录，再提交实例元数据和图标变化。
    /// </summary>
    public async Task<GameInstance> RenameInstanceAsync(
        string instanceId,
        string? newName,
        string? newIconSource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new InvalidOperationException("Instance id is required.");

        var mutationKey = instanceId.Trim();
        if (!pendingInstanceMutations.TryAdd(mutationKey, "rename"))
            throw new InvalidOperationException("Instance is already being modified.");

        try
        {
        var instances = (await GetInstancesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Instance was not found.");

        var sanitizedName = NormalizeUserInstanceName(newName);
        var normalizedIconSource = string.IsNullOrWhiteSpace(newIconSource) ? null : newIconSource.Trim();
        var persistedIconSource = normalizedIconSource;
        var currentVersionName = GetVersionName(instance);

        if (instances.Any(existing =>
                !string.Equals(existing.Id, instance.Id, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(existing.Name, sanitizedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.VersionName, sanitizedName, StringComparison.OrdinalIgnoreCase))))
        {
            throw new DuplicateGameInstanceNameException(sanitizedName);
        }

        var nameChanged = !string.Equals(currentVersionName, sanitizedName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(instance.Name, sanitizedName, StringComparison.Ordinal);
        var iconChanged = !string.Equals(instance.IconSource, normalizedIconSource, StringComparison.Ordinal);

        if (!nameChanged && !iconChanged)
            return instance;

        var directoryRenamed = !string.Equals(currentVersionName, sanitizedName, StringComparison.OrdinalIgnoreCase);
        var updatedAt = DateTimeOffset.UtcNow;
        if (directoryRenamed)
        {
            // 先完成目录及版本 JSON 的事务性重命名，再更新内存模型，失败时不会保存失配路径。
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var sourceDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, currentVersionName);
            var destinationDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, sanitizedName);
            persistedIconSource = RebaseManagedResourceProjectIcon(
                instance.IconSource,
                normalizedIconSource,
                sourceDirectory,
                destinationDirectory);
            try
            {
                await repository.RenameVersionAsync(
                    settings.MinecraftDirectory,
                    instance,
                    sanitizedName,
                    persistedIconSource,
                    updatedAt,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InstanceInstallNameConflictException)
            {
                throw new DuplicateGameInstanceNameException(sanitizedName);
            }

            instance.VersionName = sanitizedName;
            instance.InstanceDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, sanitizedName);
        }

        instance.Name = sanitizedName;
        instance.IconSource = persistedIconSource;
        instance.UpdatedAt = updatedAt;

        var index = instances.FindIndex(existing =>
            string.Equals(existing.Id, instance.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            instances[index] = instance;

        if (!directoryRenamed)
        {
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            await repository.UpdateInstanceAsync(settings.MinecraftDirectory, instance, cancellationToken)
                .ConfigureAwait(false);
        }
        logger.LogInformation(
            "Game instance renamed. InstanceId={InstanceId} VersionName={VersionName} IconChanged={IconChanged}",
            instance.Id,
            instance.VersionName,
            iconChanged);
        return instance;
        }
        finally
        {
            pendingInstanceMutations.TryRemove(mutationKey, out _);
        }
    }

    private static string? RebaseManagedResourceProjectIcon(
        string? currentIconSource,
        string? requestedIconSource,
        string sourceDirectory,
        string destinationDirectory)
    {
        if (!string.Equals(currentIconSource, requestedIconSource, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(requestedIconSource)
            || !Uri.TryCreate(requestedIconSource, UriKind.Absolute, out var iconUri)
            || !iconUri.IsFile)
        {
            return requestedIconSource;
        }

        var expectedSourcePath = Path.Combine(
            sourceDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            ResourceProjectIconFileName);
        if (!PathsEqual(iconUri.LocalPath, expectedSourcePath))
            return requestedIconSource;

        var destinationPath = Path.Combine(
            destinationDirectory,
            LauncherApplicationIdentity.StorageDirectoryName,
            ResourceProjectIconFileName);
        return new Uri(Path.GetFullPath(destinationPath)).AbsoluteUri;
    }

    public async Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = await repository.GetAllAsync(settings.MinecraftDirectory, cancellationToken).ConfigureAwait(false);
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
            return false;

        if (string.Equals(settings.DefaultInstanceId, instance.Id, StringComparison.Ordinal))
            return true;

        var persisted = false;
        await settingsService.UpdateAsync(
                latest =>
                {
                    if (!PathsEqual(latest.MinecraftDirectory, settings.MinecraftDirectory))
                        return;
                    latest.DefaultInstanceId = instance.Id;
                    persisted = true;
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!persisted)
            return false;
        settings.DefaultInstanceId = instance.Id;
        logger.LogInformation("Default game instance changed. InstanceId={InstanceId}", instance.Id);
        return true;
    }

    /// <summary>
    /// 删除实例目录和持久化记录；若删除默认实例，同时选择新的默认项。
    /// </summary>
    public async Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        var deletionKey = instanceId.Trim();
        if (!pendingInstanceMutations.TryAdd(deletionKey, "delete"))
        {
            logger.LogWarning("Duplicate game instance deletion ignored. InstanceId={InstanceId}", instanceId);
            return false;
        }

        try
        {
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var instances = (await repository.GetAllAsync(settings.MinecraftDirectory, cancellationToken)
                .ConfigureAwait(false)).ToList();
            var instance = instances.FirstOrDefault(existing =>
                string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase));

            if (instance is null)
                return false;

            var versionName = GetVersionName(instance);
            if (string.IsNullOrWhiteSpace(versionName))
                return false;

            string stagedDirectory;
            try
            {
                stagedDirectory = await repository
                    .StageVersionForDeletionAsync(
                        settings.MinecraftDirectory,
                        versionName,
                        instance.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GameInstanceMutationConflictException exception)
            {
                logger.LogWarning(
                    exception,
                    "Stale game instance deletion was rejected. InstanceId={InstanceId} VersionName={VersionName}",
                    instance.Id,
                    versionName);
                return false;
            }

            // Directory.Move is the irreversible commit point. From here on the instance must never be restored.
            instances.Remove(instance);
            await PersistDefaultAfterDeletionBestEffortAsync(settings, instances, instance).ConfigureAwait(false);

            var physicalCleanupCompleted = await repository
                .TryDeleteStagedVersionDirectoryAsync(
                    settings.MinecraftDirectory,
                    stagedDirectory,
                    CancellationToken.None)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Game instance deletion committed. InstanceId={InstanceId} VersionName={VersionName} PhysicalCleanupCompleted={PhysicalCleanupCompleted}",
                instance.Id,
                versionName,
                physicalCleanupCompleted);
            return true;
        }
        finally
        {
            pendingInstanceMutations.TryRemove(deletionKey, out _);
        }
    }

    private async Task PersistDefaultAfterDeletionBestEffortAsync(
        LauncherSettings settings,
        IReadOnlyCollection<GameInstance> remainingInstances,
        GameInstance deletedInstance)
    {
        if (!string.Equals(settings.DefaultInstanceId, deletedInstance.Id, StringComparison.OrdinalIgnoreCase))
            return;

        var replacementDefaultInstanceId = remainingInstances.FirstOrDefault()?.Id ?? string.Empty;
        try
        {
            var persisted = false;
            await settingsService.UpdateAsync(
                    latest =>
                    {
                        if (!PathsEqual(latest.MinecraftDirectory, settings.MinecraftDirectory)
                            || !string.Equals(
                                latest.DefaultInstanceId,
                                deletedInstance.Id,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        latest.DefaultInstanceId = replacementDefaultInstanceId;
                        persisted = true;
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!persisted)
                return;
            settings.DefaultInstanceId = replacementDefaultInstanceId;
            logger.LogInformation(
                "Default game instance changed after deletion. DefaultInstanceId={DefaultInstanceId}",
                settings.DefaultInstanceId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist default game instance after deletion commit. DeletedInstanceId={DeletedInstanceId} DefaultInstanceId={DefaultInstanceId}",
                deletedInstance.Id,
                settings.DefaultInstanceId);
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;
        var normalizedFirst = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var normalizedSecond = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameVersionIdentity(GameInstance instance, string versionName)
    {
        return string.Equals(instance.Name, versionName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.VersionName, versionName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 保留仍存在或正在安装的已存实例，补充新发现版本，并移除失效或重复记录。
    /// </summary>
}
