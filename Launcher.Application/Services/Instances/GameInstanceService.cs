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

using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Repositories;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

/// <summary>
/// 以磁盘中实际安装的版本为准同步实例清单，并协调实例元数据、默认实例与版本目录操作。
/// </summary>
public sealed class GameInstanceService : IGameInstanceService
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceRepository repository;
    private readonly IGameInstallCoordinator installCoordinator;
    private readonly GameInstanceCreationCoordinator creationCoordinator;
    private readonly ILogger<GameInstanceService> logger;

    public GameInstanceService(
        ISettingsService settingsService,
        IGameInstanceRepository repository,
        IEnumerable<ILoaderProvider> providers,
        IModrinthService? modrinthService = null,
        IModpackGameInstaller? modpackGameInstaller = null,
        ILogger<GameInstanceService>? logger = null,
        IGameInstallCoordinator? installCoordinator = null,
        IVersionDirectoryState? versionDirectoryState = null)
    {
        this.settingsService = settingsService;
        this.repository = repository;
        this.installCoordinator = installCoordinator ?? new GameInstallCoordinator();
        this.logger = logger ?? NullLogger<GameInstanceService>.Instance;
        var providerMap = providers.ToDictionary(provider => provider.Kind);
        creationCoordinator = new GameInstanceCreationCoordinator(
            settingsService,
            repository,
            providerMap,
            modrinthService,
            modpackGameInstaller,
            this.installCoordinator,
            versionDirectoryState ?? new RepositoryVersionDirectoryState(repository),
            GetInstancesCoreAsync,
            this.logger);
    }

    public async Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        logger.LogDebug(
            "Game instances refreshed. Count={InstanceCount} DefaultInstanceId={DefaultInstanceId}",
            instances.Count,
            settings.DefaultInstanceId);
        return instances;
    }

    public async Task<IReadOnlyList<GameInstance>> GetStoredInstancesAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken = default)
    {
        var instances = await repository.GetAllAsync(settings.MinecraftDirectory, cancellationToken).ConfigureAwait(false);
        logger.LogDebug(
            "Stored game instances loaded. Count={InstanceCount} DefaultInstanceId={DefaultInstanceId}",
            instances.Count,
            settings.DefaultInstanceId);
        return instances;
    }

    /// <summary>
    /// 合并持久化实例与磁盘版本发现结果，并在必要时修正实例清单和默认实例。
    /// </summary>
    private async Task<IReadOnlyList<GameInstance>> GetInstancesCoreAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        var storedInstances = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        // 安装中的目录可能只包含半成品元数据，完成前不能被发现流程持久化为正式实例。
        var installedVersions = (await repository
            .DiscoverInstalledVersionsAsync(settings.MinecraftDirectory, cancellationToken)
            .ConfigureAwait(false))
            .Where(version => !installCoordinator.IsInstallingVersion(settings.MinecraftDirectory, version.VersionName))
            .ToList();
        var syncedInstances = SynchronizeInstalledInstances(
            storedInstances,
            installedVersions,
            settings,
            out var instancesChanged);

        var defaultChanged = false;
        if (!string.IsNullOrWhiteSpace(settings.DefaultInstanceId)
            && syncedInstances.All(instance => instance.Id != settings.DefaultInstanceId))
        {
            settings.DefaultInstanceId = syncedInstances.FirstOrDefault()?.Id ?? string.Empty;
            defaultChanged = true;
        }

        if (instancesChanged)
        {
            logger.LogDebug(
                "Persisting synchronized game instances. StoredCount={StoredCount} InstalledCount={InstalledCount} SyncedCount={SyncedCount}",
                storedInstances.Count,
                installedVersions.Count,
                syncedInstances.Count);
            await repository.SaveAllAsync(syncedInstances, cancellationToken).ConfigureAwait(false);
        }

        if (defaultChanged)
        {
            logger.LogDebug("Default game instance reset during synchronization. DefaultInstanceId={DefaultInstanceId}", settings.DefaultInstanceId);
            await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        return syncedInstances;
    }

    public async Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        return instances.FirstOrDefault(instance => instance.Id == settings.DefaultInstanceId)
            ?? instances.FirstOrDefault();
    }

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
        var instances = (await GetInstancesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var index = instances.FindIndex(existing => existing.Id == instance.Id);
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        if (index >= 0)
            instances[index] = instance;
        else
            instances.Add(instance);

        await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);
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

        var instances = (await GetInstancesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Instance was not found.");

        var sanitizedName = NormalizeUserInstanceName(newName);
        var normalizedIconSource = string.IsNullOrWhiteSpace(newIconSource) ? null : newIconSource.Trim();
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

        if (!string.Equals(currentVersionName, sanitizedName, StringComparison.OrdinalIgnoreCase))
        {
            // 先完成目录及版本 JSON 的事务性重命名，再更新内存模型，失败时不会保存失配路径。
            var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            await repository.RenameVersionAsync(
                settings.MinecraftDirectory,
                currentVersionName,
                sanitizedName,
                cancellationToken).ConfigureAwait(false);

            instance.VersionName = sanitizedName;
            instance.InstanceDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, sanitizedName);
        }

        instance.Name = sanitizedName;
        instance.IconSource = normalizedIconSource;
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        var index = instances.FindIndex(existing =>
            string.Equals(existing.Id, instance.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            instances[index] = instance;

        await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Game instance renamed. InstanceId={InstanceId} VersionName={VersionName} IconChanged={IconChanged}",
            instance.Id,
            instance.VersionName,
            iconChanged);
        return instance;
    }

    public async Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
            return false;

        if (string.Equals(settings.DefaultInstanceId, instance.Id, StringComparison.Ordinal))
            return true;

        settings.DefaultInstanceId = instance.Id;
        await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
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

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var instances = (await GetInstancesCoreAsync(settings, cancellationToken).ConfigureAwait(false)).ToList();
        var instance = instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
            return false;

        instances.Remove(instance);

        var versionName = GetVersionName(instance);
        if (!string.IsNullOrWhiteSpace(versionName))
            repository.DeleteVersionDirectory(settings.MinecraftDirectory, versionName);

        await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Game instance deleted. InstanceId={InstanceId} VersionName={VersionName}", instance.Id, versionName);

        if (string.Equals(settings.DefaultInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase))
        {
            var nextDefaultInstance = instances.FirstOrDefault();
            settings.DefaultInstanceId = nextDefaultInstance?.Id ?? string.Empty;
            await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Default game instance changed after deletion. DefaultInstanceId={DefaultInstanceId}", settings.DefaultInstanceId);
        }

        return true;
    }

    private static bool IsSameVersionIdentity(GameInstance instance, string versionName)
    {
        return string.Equals(instance.Name, versionName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.VersionName, versionName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 保留仍存在或正在安装的已存实例，补充新发现版本，并移除失效或重复记录。
    /// </summary>
    private List<GameInstance> SynchronizeInstalledInstances(
        List<GameInstance> instances,
        IReadOnlyList<InstalledGameVersion> installedVersions,
        LauncherSettings settings,
        out bool changed)
    {
        // 已存实例保留用户配置，但必须能匹配实际版本目录；未登记目录则生成稳定 ID 的发现实例。
        changed = false;
        var syncedInstances = new List<GameInstance>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedByVersion = installedVersions
            .Where(version => !string.IsNullOrWhiteSpace(version.VersionName))
            .GroupBy(version => version.VersionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            var versionName = GetVersionName(instance);
            if (!string.IsNullOrWhiteSpace(versionName)
                && IsInstallingVersion(settings.MinecraftDirectory, versionName))
            {
                // 安装租约存在时保留原记录，即使磁盘元数据暂时不可读，也不能把实例当作已删除。
                if (seenVersions.Add(versionName))
                    syncedInstances.Add(instance);

                continue;
            }

            if (string.IsNullOrWhiteSpace(versionName)
                || !installedByVersion.TryGetValue(versionName, out var installedVersion)
                || !seenVersions.Add(installedVersion.VersionName))
            {
                changed = true;
                continue;
            }

            changed |= ApplyInstalledVersion(instance, installedVersion);
            repository.CreateInstanceDirectories(installedVersion.Directory);
            syncedInstances.Add(instance);
        }

        foreach (var installedVersion in installedVersions)
        {
            if (string.IsNullOrWhiteSpace(installedVersion.VersionName)
                || !seenVersions.Add(installedVersion.VersionName))
            {
                continue;
            }

            repository.CreateInstanceDirectories(installedVersion.Directory);
            syncedInstances.Add(CreateDiscoveredInstance(installedVersion, settings));
            changed = true;
        }

        return syncedInstances;
    }

    private bool IsInstallingVersion(string minecraftDirectory, string versionName)
    {
        return installCoordinator.IsInstallingVersion(minecraftDirectory, versionName);
    }

    private GameInstance CreateDiscoveredInstance(
        InstalledGameVersion installedVersion,
        LauncherSettings settings)
    {
        return new GameInstance
        {
            Id = CreateDiscoveredInstanceId(settings.MinecraftDirectory, installedVersion.VersionName),
            Name = installedVersion.VersionName,
            MinecraftVersion = installedVersion.MinecraftVersion,
            Loader = installedVersion.Loader,
            LoaderVersion = installedVersion.LoaderVersion,
            VersionName = installedVersion.VersionName,
            VersionType = installedVersion.VersionType,
            InstanceDirectory = installedVersion.Directory,
            MemoryMb = settings.DefaultMemoryMb,
            CreatedAt = installedVersion.DiscoveredAt,
            UpdatedAt = installedVersion.DiscoveredAt
        };
    }

    /// <summary>
    /// 用磁盘发现的权威版本信息更新实例，同时保留名称、图标等用户配置。
    /// </summary>
    private static bool ApplyInstalledVersion(GameInstance instance, InstalledGameVersion installedVersion)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(instance.Name))
            changed |= SetIfChanged(instance.Name, installedVersion.VersionName, value => instance.Name = value ?? string.Empty);
        changed |= SetIfChanged(
            instance.MinecraftVersion,
            ResolvePreferredMinecraftVersion(instance.MinecraftVersion, installedVersion.MinecraftVersion, installedVersion.VersionName),
            value => instance.MinecraftVersion = value ?? string.Empty);
        changed |= SetIfChanged(instance.VersionName, installedVersion.VersionName, value => instance.VersionName = value ?? string.Empty);
        changed |= SetIfChanged(instance.VersionType, installedVersion.VersionType, value => instance.VersionType = value ?? string.Empty);
        changed |= SetIfChanged(instance.LoaderVersion, installedVersion.LoaderVersion, value => instance.LoaderVersion = value);
        changed |= SetIfChanged(instance.InstanceDirectory, installedVersion.Directory, value => instance.InstanceDirectory = value ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (instance.Loader != installedVersion.Loader)
        {
            instance.Loader = installedVersion.Loader;
            changed = true;
        }

        return changed;
    }

    private static bool SetIfChanged(
        string? currentValue,
        string? nextValue,
        Action<string?> setValue,
        StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.Equals(currentValue, nextValue, comparison))
            return false;

        setValue(nextValue);
        return true;
    }

    private static string ResolvePreferredMinecraftVersion(
        string? currentMinecraftVersion,
        string? discoveredMinecraftVersion,
        string versionName)
    {
        if (LooksLikeMinecraftVersion(discoveredMinecraftVersion))
            return discoveredMinecraftVersion ?? string.Empty;

        if (LooksLikeMinecraftVersion(currentMinecraftVersion)
            && !string.Equals(currentMinecraftVersion, versionName, StringComparison.OrdinalIgnoreCase))
        {
            return currentMinecraftVersion ?? string.Empty;
        }

        return discoveredMinecraftVersion ?? string.Empty;
    }

    private static bool LooksLikeMinecraftVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Version.TryParse(value, out _))
            return true;

        if (value.Length >= 6
            && char.IsDigit(value[0])
            && char.IsDigit(value[1])
            && value[2] == 'w'
            && char.IsDigit(value[3])
            && char.IsDigit(value[4]))
        {
            return true;
        }

        return value.StartsWith("a", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("b", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateDiscoveredInstanceId(string minecraftDirectory, string versionName)
    {
        var versionPath = Path.GetFullPath(Path.Combine(minecraftDirectory, "versions", versionName))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(versionPath));
        return $"local-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static string GetVersionName(GameInstance instance)
    {
        return string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;
    }

    private static string NormalizeUserInstanceName(string? value)
    {
        return VersionDirectoryName.NormalizeUserInput(value);
    }

    private sealed class RepositoryVersionDirectoryState(IGameInstanceRepository repository) : IVersionDirectoryState
    {
        public bool Exists(string minecraftDirectory, string versionName)
        {
            return repository.IsInstanceInstalled(
                new GameInstance
                {
                    Name = versionName,
                    MinecraftVersion = versionName,
                    VersionName = versionName
                },
                minecraftDirectory);
        }
    }
}
