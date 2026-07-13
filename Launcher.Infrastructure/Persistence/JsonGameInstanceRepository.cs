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

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Launcher.Application;
using Launcher.Application.Repositories;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

/// <summary>
/// 持久化实例设置，并从 Minecraft 版本目录的多种历史元数据格式中发现已安装实例。
/// </summary>
public sealed class JsonGameInstanceRepository : IGameInstanceRepository
{
    private readonly ISettingsService settingsService;
    private readonly ILogger<JsonGameInstanceRepository> logger;
    private readonly GameInstanceSettingsStore instanceSettingsStore;
    private readonly VersionDirectoryManager directoryManager;
    private readonly VersionRenameTransaction renameTransaction;
    private readonly VersionDeletionManager deletionManager;
    private readonly InstalledVersionMetadataReader metadataReader;
    private readonly SemaphoreSlim ioLock = new(1, 1);

    public JsonGameInstanceRepository(
        ISettingsService settingsService,
        ILogger<JsonGameInstanceRepository>? logger = null,
        Func<string, string, CancellationToken, Task>? moveDirectoryAsync = null)
        : this(settingsService, logger, moveDirectoryAsync, null, null, null, null)
    {
    }

    internal JsonGameInstanceRepository(
        ISettingsService settingsService,
        ILogger<JsonGameInstanceRepository>? logger,
        Func<string, string, CancellationToken, Task>? moveDirectoryAsync,
        Func<Guid>? deletionGuidFactory,
        Action<string, string>? stageDeletionMove,
        Action<string, bool>? deleteStagedDirectory,
        Action<string>? recycleStagedDirectory = null,
        Func<Guid>? renameGuidFactory = null,
        Action<string>? deleteRenameMarker = null,
        Action<string, string>? quarantineRenameMarker = null,
        Action<string, string>? beforeStageDeletionMove = null,
        Action<string, string>? beforeOwnedRenameMove = null)
    {
        this.settingsService = settingsService;
        this.logger = logger ?? NullLogger<JsonGameInstanceRepository>.Instance;
        instanceSettingsStore = new GameInstanceSettingsStore(this.logger);
        directoryManager = new VersionDirectoryManager(this.logger);
        renameTransaction = new VersionRenameTransaction(
            directoryManager,
            instanceSettingsStore,
            this.logger,
            moveDirectoryAsync,
            renameGuidFactory,
            deleteRenameMarker,
            quarantineRenameMarker,
            beforeOwnedRenameMove);
        deletionManager = new VersionDeletionManager(
            directoryManager,
            this.logger,
            deletionGuidFactory,
            stageDeletionMove,
            deleteStagedDirectory,
            recycleStagedDirectory,
            beforeStageDeletionMove);
        metadataReader = new InstalledVersionMetadataReader(this.logger);
    }

    public async Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        return await GetAllAsync(settings.MinecraftDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在仓库 I/O 锁内读取指定 Minecraft 目录的实例设置快照。
    /// </summary>
    public async Task<IReadOnlyList<GameInstance>> GetAllAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        // 读取与保存共享同一锁，避免保存期间读取到被替换中的实例设置文件。
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            var instances = await instanceSettingsStore.LoadAsync(minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
            logger.LogDebug(
                "Game instances loaded. Count={InstanceCount} MinecraftDirectory={MinecraftDirectory}",
                instances.Count,
                minecraftDirectory);
            return instances;
        }
        finally
        {
            ioLock.Release();
        }
    }

    /// <summary>
    /// 串行保存实例集合，避免并发读取或写入观察到不完整状态。
    /// </summary>
    public async Task SaveAllAsync(IReadOnlyCollection<GameInstance> instances, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        await SaveAllAsync(settings.MinecraftDirectory, instances, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAllAsync(
        string minecraftDirectory,
        IReadOnlyCollection<GameInstance> instances,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            await using var mutationLock = await AcquireVersionMutationLockAsync(
                    minecraftDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            var persistedCount = await instanceSettingsStore.SaveAsync(
                    instances,
                    minecraftDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            logger.LogDebug(
                "Game instances saved. RequestedCount={RequestedCount} PersistedCount={PersistedCount} MinecraftDirectory={MinecraftDirectory}",
                instances.Count,
                persistedCount,
                minecraftDirectory);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task UpdateInstanceAsync(
        string minecraftDirectory,
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var mutationLock = await AcquireVersionMutationLockAsync(minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
            await instanceSettingsStore.UpdateAsync(instance, minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public Task<IReadOnlyList<InstalledGameVersion>> DiscoverInstalledVersionsAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        return metadataReader.DiscoverAsync(minecraftDirectory, cancellationToken);
    }

    public string GetUniqueInstanceDirectory(string dataDirectory, string name)
    {
        return directoryManager.GetUniqueInstanceDirectory(dataDirectory, name);
    }

    public string GetVersionDirectory(string minecraftDirectory, string versionName)
    {
        return directoryManager.GetVersionDirectory(minecraftDirectory, versionName);
    }

    public bool IsInstanceInstalled(GameInstance instance, string minecraftDirectory)
    {
        var versionName = GetVersionName(instance);
        if (string.IsNullOrWhiteSpace(versionName))
            return false;

        return metadataReader.Exists(GetVersionDirectory(minecraftDirectory, versionName), versionName);
    }

    public void CreateInstanceDirectories(string directory)
    {
        directoryManager.CreateInstanceDirectories(directory);
    }

    public void DeleteVersionDirectory(string minecraftDirectory, string versionName)
    {
        directoryManager.DeleteVersionDirectory(minecraftDirectory, versionName);
    }

    public async Task<string> StageVersionForDeletionAsync(
        string minecraftDirectory,
        string versionName,
        string expectedInstanceId,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var mutationLock = await AcquireVersionMutationLockAsync(minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
            var sourceDirectory = directoryManager.GetVersionDirectory(minecraftDirectory, versionName);
            await instanceSettingsStore.EnsureIdentityAsync(sourceDirectory, expectedInstanceId, cancellationToken)
                .ConfigureAwait(false);
            return await deletionManager.StageAsync(
                    minecraftDirectory,
                    versionName,
                    expectedInstanceId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task<bool> TryDeleteStagedVersionDirectoryAsync(
        string minecraftDirectory,
        string stagedDirectory,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var mutationLock = await AcquireVersionMutationLockAsync(minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return deletionManager.TryDelete(minecraftDirectory, stagedDirectory);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task CleanupStagedVersionDirectoriesAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var mutationLock = await AcquireVersionMutationLockAsync(minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
            deletionManager.CleanupPending(minecraftDirectory, cancellationToken);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task RenameVersionAsync(
        string minecraftDirectory,
        GameInstance instance,
        string newVersionName,
        string? newIconSource,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var mutationLock = await AcquireVersionMutationLockAsync(minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
            await renameTransaction.ExecuteAsync(
                minecraftDirectory,
                instance,
                newVersionName,
                newIconSource,
                updatedAt,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task RecoverPendingVersionRenamesAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var mutationLock = await AcquireVersionMutationLockAsync(minecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
            await renameTransaction.RecoverAllAsync(minecraftDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    private static string GetVersionName(GameInstance instance)
    {
        return string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;
    }

    private static Task<FileStream> AcquireVersionMutationLockAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetFullPath(Path.Combine(minecraftDirectory, "versions")));
        return CrossProcessVersionLock.AcquireAsync(
            CrossProcessVersionLock.GetMutationPath(minecraftDirectory),
            progress: null,
            cancellationToken);
    }

}

/// <summary>
/// 以确定性的回退顺序识别官方、旧版及第三方 Loader 生成的版本元数据。
/// </summary>
