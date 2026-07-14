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

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

public sealed partial class ForgeLoaderProvider
{
private async Task<string> InstallCoreAsync(
        string minecraftVersion,
        string gameDirectory,
        string sharedMinecraftDirectory,
        string isolatedVersionName,
        string? loaderVersion,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond)
    {
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));
        using var speedReporter = new SlidingWindowDownloadSpeedReporter(progress);
        var (selectedLoaderVersion, catalogEntry) = await ResolveCatalogEntryAsync(
            minecraftVersion,
            loaderVersion,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);

        // 安装器会创建名称不可完全预测的中间版本；快照用于失败和成功后的精确清理。
        var existingVersionNames = LoaderVersionDirectoryTransaction.CaptureExistingVersions(gameDirectory);
        var installerSessionDirectory = Path.Combine(tempRootDirectory, "launcher-forge", Guid.NewGuid().ToString("N"));
        var installerJarPath = Path.Combine(installerSessionDirectory, $"forge-{minecraftVersion}-{selectedLoaderVersion}-installer.jar");
        var installerMinecraftDirectory = Path.Combine(installerSessionDirectory, ".minecraft");
        Directory.CreateDirectory(installerSessionDirectory);

        try
        {
            LoaderVersionDirectoryTransaction.EnsureLauncherProfileExists(installerMinecraftDirectory);

            progress?.Report(new LauncherProgress(InstallProgressStages.DownloadingLoaderInstaller, string.Empty));
            await DownloadInstallerAsync(
                catalogEntry.InstallerUrl,
                installerJarPath,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond,
                speedReporter);

            var prerequisiteSeeder = new LoaderInstallerPrerequisiteSeeder(logger);
            var workspaceSnapshot = await prerequisiteSeeder.SeedAsync(
                sharedMinecraftDirectory,
                installerMinecraftDirectory,
                minecraftVersion,
                installerJarPath,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));
            await RunForgeInstallerAsync(
                installerJarPath,
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);

            var processorArtifactService = new ForgeProcessorArtifactService(
                httpClient,
                installerRunner,
                finalVersionInstaller,
                downloadSpeedLimitState,
                logger,
                tempRootDirectory);
            var processorManifest = await processorArtifactService.ValidateInstallerOutputsAsync(
                installerJarPath,
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                cancellationToken).ConfigureAwait(false);

            var sourceVersionName = FindInstalledSourceVersionName(
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                []);

            progress?.Report(new LauncherProgress(InstallProgressStages.FinalizingVersion, string.Empty));
            var finalVersionName = await CreateFinalVersionAsync(
                installerMinecraftDirectory,
                sourceVersionName,
                isolatedVersionName,
                minecraftVersion,
                cancellationToken);

            await EnsureFinalVersionIsSelfContainedAsync(
                installerMinecraftDirectory,
                finalVersionName,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);

            progress?.Report(new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty));
            await finalVersionInstaller.InstallAsync(
                new MinecraftPath(installerMinecraftDirectory),
                finalVersionName,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);

            await LoaderVersionDirectoryTransaction.WriteForgeProcessorMetadataAsync(
                installerMinecraftDirectory,
                finalVersionName,
                processorManifest,
                cancellationToken).ConfigureAwait(false);

            // 最终版本完成扁平化、修复和文件补齐后才提交，用户目录不会看到依赖沙箱的半成品。
            LoaderVersionDirectoryTransaction.CopyFinalVersionDirectory(
                installerMinecraftDirectory,
                gameDirectory,
                finalVersionName,
                cancellationToken);
            await prerequisiteSeeder.PublishDeltaAsync(
                workspaceSnapshot,
                gameDirectory,
                cancellationToken).ConfigureAwait(false);
            await processorArtifactService.ValidateManifestAsync(
                gameDirectory,
                processorManifest,
                cancellationToken).ConfigureAwait(false);

            LoaderVersionDirectoryTransaction.CleanupCreatedVersionDirectories(gameDirectory, existingVersionNames, finalVersionName);
            return finalVersionName;
        }
        catch
        {
            LoaderVersionDirectoryTransaction.CleanupCreatedVersionDirectories(gameDirectory, existingVersionNames, preserveVersionName: null);
            throw;
        }
        finally
        {
            LoaderVersionDirectoryTransaction.TryDeleteDirectory(installerSessionDirectory);
        }
    }

    /// <summary>
    /// 通过下载源执行器加载并缓存 Forge 清单，同一 Minecraft 版本只解析一次。
    /// </summary>
    private async Task<(string LoaderVersion, ForgeCatalogEntry CatalogEntry)> ResolveCatalogEntryAsync(
        string minecraftVersion,
        string? loaderVersion,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond)
    {
        var selectedLoaderVersion = loaderVersion;
        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            var availableVersions = await GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
            selectedLoaderVersion = availableVersions.FirstOrDefault()?.Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Forge loader version available for {minecraftVersion}.");

        var catalog = await GetCatalogAsync(
            minecraftVersion,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
        if (!catalog.TryGetValue(selectedLoaderVersion, out var catalogEntry))
        {
            throw new InvalidOperationException(
                $"Forge loader version {selectedLoaderVersion} is not available for {minecraftVersion}.");
        }

        return (selectedLoaderVersion, catalogEntry);
    }

    private async Task RunForgeInstallerAsync(
        string installerJarPath,
        string installerMinecraftDirectory,
        string minecraftVersion,
        string selectedLoaderVersion,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond)
    {
        try
        {
            await installerRunner.RunInstallerAsync(
                "java",
                installerJarPath,
                installerMinecraftDirectory,
                cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsLegacyForgeInstallClientFailure(exception))
        {
            logger.LogInformation(
                exception,
                "Legacy Forge installer detected because --installClient is unsupported. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion}",
                minecraftVersion,
                selectedLoaderVersion);
            await InstallLegacyForgeClientAsync(
                installerJarPath,
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
        }
    }
}
