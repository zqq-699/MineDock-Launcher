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
using System.Net.Http;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Modpacks;

/// <summary>
/// 在临时 Minecraft 沙箱中组合整合包所需版本，再把最终版本和共享运行时内容提交到目标目录。
/// </summary>
internal sealed class ModpackGameInstaller : IModpackGameInstaller
{
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> providers;
    private readonly IFinalVersionInstaller finalVersionInstaller;
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly string tempRootDirectory;
    private readonly ILogger logger;

    public ModpackGameInstaller(
        IEnumerable<ILoaderProvider> providers,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<ModpackGameInstaller>? logger = null)
        : this(
            providers,
            new FinalVersionInstaller(),
            httpClient: null,
            downloadSpeedLimitState,
            tempRootDirectory: null,
            logger)
    {
    }

    internal ModpackGameInstaller(
        IEnumerable<ILoaderProvider> providers,
        IFinalVersionInstaller finalVersionInstaller,
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        string? tempRootDirectory = null,
        ILogger? logger = null)
    {
        this.providers = providers.ToDictionary(provider => provider.Kind);
        this.finalVersionInstaller = finalVersionInstaller;
        this.httpClient = httpClient ?? new HttpClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.tempRootDirectory = string.IsNullOrWhiteSpace(tempRootDirectory) ? Path.GetTempPath() : tempRootDirectory;
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// 根据 Loader 类型选择沙箱组合流程；Forge 系列委托其专用安装提供方。
    /// </summary>
    public Task<string> InstallLoaderAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        LoaderInstallTarget target,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        ValidateTarget(target);
        return loader switch
        {
            LoaderKind.Vanilla => InstallVanillaInSandboxAsync(
                minecraftVersion,
                target,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond),
            LoaderKind.Fabric => InstallFabricInSandboxAsync(
                minecraftVersion,
                loaderVersion,
                target,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond),
            LoaderKind.Quilt => InstallQuiltInSandboxAsync(
                minecraftVersion,
                loaderVersion,
                target,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond),
            _ => InstallInstanceAsync(
                minecraftVersion,
                loader,
                loaderVersion,
                target,
                progress,
                cancellationToken,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond)
        };
    }

    /// <summary>
    /// 调用已注册 Loader 提供方在指定目标目录安装隔离实例版本。
    /// </summary>
    public Task<string> InstallInstanceAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        LoaderInstallTarget target,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        ValidateTarget(target);
        if (!providers.TryGetValue(loader, out var provider) || !provider.IsImplemented)
            throw new NotSupportedException($"{loader} is not implemented yet.");

        return InstallProviderInSandboxAsync(
            provider,
            minecraftVersion,
            loaderVersion,
            target,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond);
    }

    private Task<string> InstallVanillaInSandboxAsync(
        string minecraftVersion,
        LoaderInstallTarget target,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        return InstallComposedVersionInSandboxAsync(
            "Vanilla",
            minecraftVersion,
            target,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            sandboxMinecraftDirectory => VanillaVersionComposer.CreateFinalVersionAsync(
                httpClient,
                minecraftVersion,
                target.LogicalVersionName,
                sandboxMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken));
    }

    private async Task<string> InstallFabricInSandboxAsync(
        string minecraftVersion,
        string? loaderVersion,
        LoaderInstallTarget target,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        var selectedLoaderVersion = loaderVersion;
        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            if (!providers.TryGetValue(LoaderKind.Fabric, out var provider))
                throw new InvalidOperationException("Fabric loader provider is not available.");

            selectedLoaderVersion = (await provider.GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false))
                .FirstOrDefault()?
                .Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Fabric loader version available for {minecraftVersion}.");

        return await InstallComposedVersionInSandboxAsync(
            "Fabric",
            minecraftVersion,
            target,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            sandboxMinecraftDirectory => FabricVersionComposer.CreateFinalVersionAsync(
                httpClient,
                minecraftVersion,
                selectedLoaderVersion,
                target.LogicalVersionName,
                sandboxMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
            cancellationToken)).ConfigureAwait(false);
    }

    private async Task<string> InstallQuiltInSandboxAsync(
        string minecraftVersion,
        string? loaderVersion,
        LoaderInstallTarget target,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        var selectedLoaderVersion = loaderVersion;
        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            if (!providers.TryGetValue(LoaderKind.Quilt, out var provider))
                throw new InvalidOperationException("Quilt loader provider is not available.");

            selectedLoaderVersion = (await provider.GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false))
                .FirstOrDefault()?
                .Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Quilt loader version available for {minecraftVersion}.");

        return await InstallComposedVersionInSandboxAsync(
            "Quilt",
            minecraftVersion,
            target,
            progress,
            cancellationToken,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            sandboxMinecraftDirectory => QuiltVersionComposer.CreateFinalVersionAsync(
                httpClient,
                minecraftVersion,
                selectedLoaderVersion,
                target.LogicalVersionName,
                sandboxMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// 播种沙箱、组合并补齐版本、提交最终目录与共享内容，最后无条件清理沙箱。
    /// </summary>
    private async Task<string> InstallComposedVersionInSandboxAsync(
        string loaderName,
        string minecraftVersion,
        LoaderInstallTarget target,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        Func<string, Task<string>> composeAsync)
    {
        // Loader 安装器只能面向完整 .minecraft 工作；沙箱隔离其中间文件，避免污染真实实例。
        var sessionDirectory = Path.Combine(tempRootDirectory, "launcher-modpack-version", Guid.NewGuid().ToString("N"));
        var sandboxMinecraftDirectory = Path.Combine(sessionDirectory, ".minecraft");
        Directory.CreateDirectory(sessionDirectory);

        logger.LogInformation(
            "Installing modpack loader version through sandbox. Loader={Loader} MinecraftVersion={MinecraftVersion} TargetMinecraftDirectory={TargetMinecraftDirectory} TargetVersionName={TargetVersionName} SessionDirectory={SessionDirectory}",
            loaderName,
            minecraftVersion,
            target.MinecraftDirectory,
            target.LogicalVersionName,
            sessionDirectory);

        try
        {
            // 先播种已有共享文件减少重复下载，安装成功后再把沙箱新增内容回写目标目录。
            var seededRuntimeCopy = MinecraftSharedContentCopier.CopySharedRuntimeContent(
                target.MinecraftDirectory,
                sandboxMinecraftDirectory,
                logger,
                cancellationToken);
            var finalVersionName = await composeAsync(sandboxMinecraftDirectory).ConfigureAwait(false);

            await finalVersionInstaller.InstallAsync(
                sandboxMinecraftDirectory,
                finalVersionName,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false);

            MinecraftVersionDirectoryCopier.CopyVersionDirectoryTo(
                sandboxMinecraftDirectory,
                finalVersionName,
                target.PhysicalOutputDirectory,
                allowExistingDestination: true,
                cancellationToken: cancellationToken);
            var appliedRuntimeCopy = MinecraftSharedContentCopier.CopySharedRuntimeContent(
                sandboxMinecraftDirectory,
                target.MinecraftDirectory,
                logger,
                cancellationToken);

            logger.LogInformation(
                "Installed modpack loader version through sandbox. Loader={Loader} MinecraftVersion={MinecraftVersion} FinalVersionName={FinalVersionName} SessionDirectory={SessionDirectory} SeededLibrariesCopied={SeededLibrariesCopied} SeededAssetIndexesCopied={SeededAssetIndexesCopied} SeededAssetObjectsCopied={SeededAssetObjectsCopied} SeededLogConfigsCopied={SeededLogConfigsCopied} AppliedLibrariesCopied={AppliedLibrariesCopied} AppliedAssetIndexesCopied={AppliedAssetIndexesCopied} AppliedAssetObjectsCopied={AppliedAssetObjectsCopied} AppliedLogConfigsCopied={AppliedLogConfigsCopied}",
                loaderName,
                minecraftVersion,
                finalVersionName,
                sessionDirectory,
                seededRuntimeCopy.LibrariesCopied,
                seededRuntimeCopy.AssetIndexesCopied,
                seededRuntimeCopy.AssetObjectsCopied,
                seededRuntimeCopy.LogConfigsCopied,
                appliedRuntimeCopy.LibrariesCopied,
                appliedRuntimeCopy.AssetIndexesCopied,
                appliedRuntimeCopy.AssetObjectsCopied,
                appliedRuntimeCopy.LogConfigsCopied);
            return finalVersionName;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Installing modpack loader version through sandbox failed. Loader={Loader} MinecraftVersion={MinecraftVersion} TargetVersionName={TargetVersionName} SessionDirectory={SessionDirectory}",
                loaderName,
                minecraftVersion,
                target.LogicalVersionName,
                sessionDirectory);
            throw;
        }
        finally
        {
            CleanupSandboxDirectory(sessionDirectory, cancellationToken.IsCancellationRequested);
        }
    }

    private async Task<string> InstallProviderInSandboxAsync(
        ILoaderProvider provider,
        string minecraftVersion,
        string? loaderVersion,
        LoaderInstallTarget target,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond)
    {
        var sessionDirectory = Path.Combine(tempRootDirectory, "launcher-instance-version", Guid.NewGuid().ToString("N"));
        var sandboxMinecraftDirectory = Path.Combine(sessionDirectory, ".minecraft");
        Directory.CreateDirectory(sessionDirectory);
        try
        {
            MinecraftSharedContentCopier.CopySharedRuntimeContent(
                target.MinecraftDirectory,
                sandboxMinecraftDirectory,
                logger,
                cancellationToken);
            var finalVersionName = await provider.InstallAsync(
                minecraftVersion,
                sandboxMinecraftDirectory,
                target.LogicalVersionName,
                loaderVersion,
                progress,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
            if (!string.Equals(finalVersionName, target.LogicalVersionName, StringComparison.Ordinal))
                throw new InvalidDataException("Loader returned a version identity different from the logical install name.");
            MinecraftVersionDirectoryCopier.CopyVersionDirectoryTo(
                sandboxMinecraftDirectory,
                finalVersionName,
                target.PhysicalOutputDirectory,
                allowExistingDestination: true,
                cancellationToken);
            MinecraftSharedContentCopier.CopySharedRuntimeContent(
                sandboxMinecraftDirectory,
                target.MinecraftDirectory,
                logger,
                cancellationToken);
            return finalVersionName;
        }
        finally
        {
            CleanupSandboxDirectory(sessionDirectory, cancellationToken.IsCancellationRequested);
        }
    }

    private void CleanupSandboxDirectory(string directory, bool deferCleanup)
    {
        if (!deferCleanup)
        {
            TryDeleteSandboxDirectory(directory);
            return;
        }

        logger.LogInformation(
            "Deferring modpack loader sandbox cleanup after cancellation. Directory={Directory}",
            directory);
        _ = Task.Run(() => TryDeleteSandboxDirectory(directory));
    }

    private void TryDeleteSandboxDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to delete modpack loader sandbox directory. Directory={Directory}",
                directory);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to delete modpack loader sandbox directory. Directory={Directory}",
                directory);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unexpected failure while deleting modpack loader sandbox directory. Directory={Directory}",
                directory);
        }
    }

    private static void ValidateTarget(LoaderInstallTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.LogicalVersionName))
            throw new ArgumentException("Logical version name is required.", nameof(target));
        var versionsDirectory = Path.GetFullPath(Path.Combine(target.MinecraftDirectory, "versions"));
        var outputDirectory = Path.GetFullPath(target.PhysicalOutputDirectory);
        if (!string.Equals(Path.GetDirectoryName(outputDirectory), versionsDirectory, StringComparison.OrdinalIgnoreCase)
            || !PendingInstanceInstallDirectory.IsPending(outputDirectory))
        {
            throw new ArgumentException("Physical version output must be a pending install directory.", nameof(target));
        }
    }
}
