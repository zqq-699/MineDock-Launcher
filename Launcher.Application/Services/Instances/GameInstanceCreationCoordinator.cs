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

using Launcher.Application.Repositories;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Application.Services;

/// <summary>
/// 串联安装租约、游戏与 Loader 安装、可选内容安装、实例持久化和失败清理。
/// </summary>
internal sealed class GameInstanceCreationCoordinator
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceRepository repository;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers;
    private readonly IModrinthService? modrinthService;
    private readonly IModpackGameInstaller gameInstaller;
    private readonly IGameInstallCoordinator installCoordinator;
    private readonly IVersionDirectoryState versionDirectoryState;
    private readonly Func<LauncherSettings, CancellationToken, Task<IReadOnlyList<GameInstance>>> loadInstancesAsync;
    private readonly ILogger logger;

    public GameInstanceCreationCoordinator(
        ISettingsService settingsService,
        IGameInstanceRepository repository,
        IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers,
        IModrinthService? modrinthService,
        IModpackGameInstaller? gameInstaller,
        IGameInstallCoordinator installCoordinator,
        IVersionDirectoryState versionDirectoryState,
        Func<LauncherSettings, CancellationToken, Task<IReadOnlyList<GameInstance>>> loadInstancesAsync,
        ILogger logger)
    {
        this.settingsService = settingsService;
        this.repository = repository;
        this.providers = providers;
        this.modrinthService = modrinthService;
        this.gameInstaller = gameInstaller ?? new LoaderProviderGameInstaller(providers);
        this.installCoordinator = installCoordinator;
        this.versionDirectoryState = versionDirectoryState;
        this.loadInstancesAsync = loadInstancesAsync;
        this.logger = logger;
    }

    /// <summary>
    /// 在独占安装租约中完成版本安装、可选内容安装、实例持久化和默认实例初始化。
    /// </summary>
    public async Task<GameInstance> CreateAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string? name,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        bool installFabricApi,
        string? fabricApiVersionId,
        string? quiltStandardLibraryVersionId)
    {
        if (!providers.TryGetValue(loader, out var provider) || !provider.IsImplemented)
            throw new NotSupportedException($"{loader} is not implemented yet.");

        logger.LogInformation(
            "Creating game instance. MinecraftVersion={MinecraftVersion} Loader={Loader} LoaderVersion={LoaderVersion} RequestedName={RequestedName}",
            minecraftVersion,
            loader,
            loaderVersion,
            name);

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var versionIdentity = ResolveVersionIdentity(minecraftVersion, loader, loaderVersion, name);
        // 租约同时用于串行化同名安装，并让实例发现流程识别尚未完成的版本目录。
        await using var installLease = await installCoordinator
            .AcquireInstallAsync(settings.MinecraftDirectory, versionIdentity, progress, cancellationToken)
            .ConfigureAwait(false);
        if (progress is not null)
            logger.LogDebug("Game instance install acquired coordinator lease. VersionIdentity={VersionIdentity}", versionIdentity);

        // 安装前记录目录是否已存在，失败时只能删除本次新建的目录，绝不能损坏用户已有版本。
        var cleanupCandidates = CreateCleanupCandidates(
            settings.MinecraftDirectory,
            minecraftVersion,
            versionIdentity,
            loader);
        var instances = (await loadInstancesAsync(settings, cancellationToken).ConfigureAwait(false)).ToList();
        if (instances.Any(instance => IsSameVersionIdentity(instance, versionIdentity)))
            throw new DuplicateGameInstanceNameException(versionIdentity);

        try
        {
            var versionName = await gameInstaller.InstallInstanceAsync(
                minecraftVersion,
                loader,
                loaderVersion,
                settings.MinecraftDirectory,
                versionIdentity,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
            logger.LogInformation(
                "Game version installed. VersionIdentity={VersionIdentity} VersionName={VersionName} Loader={Loader}",
                versionIdentity,
                versionName,
                loader);

            var instance = CreateInstance(settings, minecraftVersion, loader, loaderVersion, versionIdentity, versionName);
            await InstallOptionalContentAsync(
                    instance,
                    installFabricApi,
                    fabricApiVersionId,
                    quiltStandardLibraryVersionId,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            instances.Add(instance);
            await repository.SaveAllAsync(instances, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(settings.DefaultInstanceId))
            {
                settings.DefaultInstanceId = instance.Id;
                await settingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Default game instance initialized. InstanceId={InstanceId}", instance.Id);
            }

            logger.LogInformation(
                "Game instance created. InstanceId={InstanceId} VersionName={VersionName} InstanceDirectory={InstanceDirectory}",
                instance.Id,
                instance.VersionName,
                instance.InstanceDirectory);
            return instance;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Game instance creation failed. VersionIdentity={VersionIdentity} Loader={Loader}",
                versionIdentity,
                loader);
            TryCleanupCreatedVersionDirectories(settings.MinecraftDirectory, cleanupCandidates);
            throw;
        }
    }

    private GameInstance CreateInstance(
        LauncherSettings settings,
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string versionIdentity,
        string versionName)
    {
        var instanceDirectory = repository.GetVersionDirectory(settings.MinecraftDirectory, versionName);
        repository.CreateInstanceDirectories(instanceDirectory);
        var now = DateTimeOffset.UtcNow;
        return new GameInstance
        {
            Name = versionIdentity,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loader == LoaderKind.Vanilla ? null : loaderVersion,
            VersionName = versionName,
            VersionType = string.Empty,
            InstanceDirectory = instanceDirectory,
            MemoryMb = settings.DefaultMemoryMb,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 根据 Loader 选择安装 Fabric API 或 Quilt 标准库；未选择时保持纯 Loader 实例。
    /// </summary>
    private async Task InstallOptionalContentAsync(
        GameInstance instance,
        bool installFabricApi,
        string? fabricApiVersionId,
        string? quiltStandardLibraryVersionId,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (instance.Loader is LoaderKind.Fabric
            && installFabricApi
            && !string.IsNullOrWhiteSpace(fabricApiVersionId))
        {
            if (modrinthService is null)
                throw new InvalidOperationException("Modrinth service is required to install Fabric API.");

            logger.LogInformation(
                "Installing Fabric API for game instance. InstanceId={InstanceId} VersionId={VersionId}",
                instance.Id,
                fabricApiVersionId);
            await modrinthService.InstallFabricApiAsync(instance, fabricApiVersionId, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        if (instance.Loader is not LoaderKind.Quilt || string.IsNullOrWhiteSpace(quiltStandardLibraryVersionId))
            return;

        if (modrinthService is null)
            throw new InvalidOperationException("Modrinth service is required to install QFAPI/QSL.");

        logger.LogInformation(
            "Installing Quilt standard library for game instance. InstanceId={InstanceId} VersionId={VersionId}",
            instance.Id,
            quiltStandardLibraryVersionId);
        await modrinthService.InstallQuiltStandardLibraryAsync(
                instance,
                quiltStandardLibraryVersionId,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolveVersionIdentity(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string? requestedName)
    {
        return string.IsNullOrWhiteSpace(requestedName)
            ? VersionDirectoryName.Sanitize(CreateDefaultInstanceName(minecraftVersion, loader, loaderVersion))
            : VersionDirectoryName.NormalizeUserInput(requestedName);
    }

    private static string CreateDefaultInstanceName(string minecraftVersion, LoaderKind loader, string? loaderVersion)
    {
        return loader switch
        {
            LoaderKind.Fabric when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-fabric-{loaderVersion}",
            LoaderKind.Fabric => $"{minecraftVersion}-fabric",
            LoaderKind.Forge when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-forge-{loaderVersion}",
            LoaderKind.Forge => $"{minecraftVersion}-forge",
            LoaderKind.NeoForge when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-neoforge-{loaderVersion}",
            LoaderKind.NeoForge => $"{minecraftVersion}-neoforge",
            LoaderKind.Quilt when !string.IsNullOrWhiteSpace(loaderVersion) => $"{minecraftVersion}-quilt-{loaderVersion}",
            LoaderKind.Quilt => $"{minecraftVersion}-quilt",
            _ => minecraftVersion
        };
    }

    /// <summary>
    /// 在安装前记录可能被创建的版本目录及其原始存在状态，供失败补偿使用。
    /// </summary>
    private IReadOnlyList<VersionCleanupCandidate> CreateCleanupCandidates(
        string minecraftDirectory,
        string minecraftVersion,
        string versionIdentity,
        LoaderKind loader)
    {
        var versionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { versionIdentity };
        if (loader is LoaderKind.Vanilla)
            versionNames.Add(minecraftVersion);

        return versionNames
            .Select(versionName => new VersionCleanupCandidate(
                versionName,
                versionDirectoryState.Exists(minecraftDirectory, versionName)))
            .ToArray();
    }

    /// <summary>
    /// 尽力删除本次安装新建的目录，同时绝不删除安装前已有目录。
    /// </summary>
    private void TryCleanupCreatedVersionDirectories(
        string minecraftDirectory,
        IReadOnlyList<VersionCleanupCandidate> cleanupCandidates)
    {
        // 清理是原始失败后的尽力补偿；清理异常不得掩盖真正的安装异常。
        foreach (var candidate in cleanupCandidates.Where(candidate => !candidate.ExistedBeforeInstall))
        {
            try
            {
                repository.DeleteVersionDirectory(minecraftDirectory, candidate.VersionName);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsSameVersionIdentity(GameInstance instance, string versionName)
    {
        return string.Equals(instance.VersionName, versionName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.Name, versionName, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record VersionCleanupCandidate(string VersionName, bool ExistedBeforeInstall);

    private sealed class LoaderProviderGameInstaller(IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers)
        : IModpackGameInstaller
    {
        public Task InstallMinecraftBaseAsync(
            string minecraftVersion,
            string gameDirectory,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            throw new NotSupportedException("InstallMinecraftBaseAsync requires the infrastructure modpack game installer.");
        }

        public Task<string> InstallLoaderAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string gameDirectory,
            string isolatedVersionName,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return InstallInstanceAsync(
                minecraftVersion,
                loader,
                loaderVersion,
                gameDirectory,
                isolatedVersionName,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond);
        }

        public Task<string> InstallInstanceAsync(
            string minecraftVersion,
            LoaderKind loader,
            string? loaderVersion,
            string gameDirectory,
            string isolatedVersionName,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            if (!providers.TryGetValue(loader, out var provider) || !provider.IsImplemented)
                throw new NotSupportedException($"{loader} is not implemented yet.");

            return provider.InstallAsync(
                minecraftVersion,
                gameDirectory,
                isolatedVersionName,
                loaderVersion,
                progress,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
        }
    }
}
