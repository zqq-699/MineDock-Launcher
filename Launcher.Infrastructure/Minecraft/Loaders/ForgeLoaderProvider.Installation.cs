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
        var catalogStopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Forge installation catalog resolution started. MinecraftVersion={MinecraftVersion} RequestedLoaderVersion={RequestedLoaderVersion}",
            minecraftVersion,
            loaderVersion);
        var (selectedLoaderVersion, catalogEntry) = await ResolveCatalogEntryAsync(
            minecraftVersion,
            loaderVersion,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
        logger.LogInformation(
            "Forge installation catalog resolution completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
            minecraftVersion,
            selectedLoaderVersion,
            catalogStopwatch.ElapsedMilliseconds);

        // 安装器会创建名称不可完全预测的中间版本；快照用于失败和成功后的精确清理。
        var existingVersionNames = LoaderVersionDirectoryTransaction.CaptureExistingVersions(gameDirectory);
        var installerSessionDirectory = Path.Combine(tempRootDirectory, "launcher-forge", Guid.NewGuid().ToString("N"));
        var installerJarPath = Path.Combine(installerSessionDirectory, $"forge-{minecraftVersion}-{selectedLoaderVersion}-installer.jar");
        var installerMinecraftDirectory = Path.Combine(installerSessionDirectory, ".minecraft");
        var installPathLayout = MinecraftInstallPathLayout.Create(
            installerMinecraftDirectory,
            sharedMinecraftDirectory);
        using var downloadOperation = VanillaLoaderProvider.CreateDownloadOperationContext(installPathLayout.Path);
        Directory.CreateDirectory(installerSessionDirectory);

        try
        {
            LoaderVersionDirectoryTransaction.EnsureLauncherProfileExists(installerMinecraftDirectory);

            progress?.Report(new LauncherProgress(InstallProgressStages.DownloadingLoaderInstaller, string.Empty));
            var installerDownloadStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Forge installer download started. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion}",
                minecraftVersion,
                selectedLoaderVersion);
            await DownloadInstallerAsync(
                catalogEntry.InstallerUrl,
                installerJarPath,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond,
                speedReporter);
            logger.LogInformation(
                "Forge installer download completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                installerDownloadStopwatch.ElapsedMilliseconds);

            var installerArtifactService = new LoaderInstallerArtifactService(
                httpClient,
                installerRunner,
                finalVersionInstaller,
                downloadSpeedLimitState,
                logger,
                tempRootDirectory);
            var planReadStopwatch = Stopwatch.StartNew();
            var installerPlan = await installerArtifactService.ReadPlanAsync(installerJarPath, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Forge installer plan read completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                planReadStopwatch.ElapsedMilliseconds);

            var prerequisiteSeeder = new LoaderInstallerPrerequisiteSeeder(logger);
            var prerequisitesStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Forge installer prerequisites preparation started. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion}",
                minecraftVersion,
                selectedLoaderVersion);
            var workspaceSnapshot = await prerequisiteSeeder.SeedAsync(
                sharedMinecraftDirectory,
                installerMinecraftDirectory,
                minecraftVersion,
                installerJarPath,
                cancellationToken).ConfigureAwait(false);
            await installerArtifactService.MaterializePrerequisitesAsync(
                installerJarPath,
                installerPlan,
                installerMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                cancellationToken,
                downloadOperation).ConfigureAwait(false);
            logger.LogInformation(
                "Forge installer prerequisites preparation completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                prerequisitesStopwatch.ElapsedMilliseconds);

            progress?.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));
            var installerRunStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Forge Java installer process started. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion}",
                minecraftVersion,
                selectedLoaderVersion);
            await RunForgeInstallerAsync(
                installerJarPath,
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
            logger.LogInformation(
                "Forge Java installer process completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                installerRunStopwatch.ElapsedMilliseconds);

            var sandboxValidationStopwatch = Stopwatch.StartNew();
            await installerArtifactService.ValidatePublishedArtifactsAsync(
                installerMinecraftDirectory,
                installerPlan,
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Forge installer sandbox artifact validation completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                sandboxValidationStopwatch.ElapsedMilliseconds);

            var sourceVersionName = FindInstalledSourceVersionName(
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                []);

            progress?.Report(new LauncherProgress(InstallProgressStages.FinalizingVersion, string.Empty));
            var finalizationStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Forge final version preparation started. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion}",
                minecraftVersion,
                selectedLoaderVersion);
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
            await LoaderInstallerArtifactService.ApplyRuntimeLibrariesAsync(
                Path.Combine(installerMinecraftDirectory, "versions", finalVersionName, $"{finalVersionName}.json"),
                installerPlan,
                "forgeProcessorArtifacts",
                cancellationToken).ConfigureAwait(false);

            var sharedPublicationStopwatch = Stopwatch.StartNew();
            await prerequisiteSeeder.PublishDeltaAsync(
                workspaceSnapshot,
                sharedMinecraftDirectory,
                cancellationToken,
                LoaderInstallerArtifactService.CreateTrustedSharedLibraryExpectations(installerPlan),
                downloadOperation).ConfigureAwait(false);
            logger.LogInformation(
                "Forge Java sandbox shared output publication completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                sharedPublicationStopwatch.ElapsedMilliseconds);
            logger.LogInformation(
                "Forge final version preparation completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} VersionName={VersionName} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                finalVersionName,
                finalizationStopwatch.ElapsedMilliseconds);

            progress?.Report(new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty));
            var finalContentStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Forge final version content installation started. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} VersionName={VersionName}",
                minecraftVersion,
                selectedLoaderVersion,
                finalVersionName);
            await finalVersionInstaller.InstallAsync(
                installPathLayout.Path,
                finalVersionName,
                downloadOperation,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
            await installerArtifactService.MaterializeRuntimeLibrariesAsync(
                installerJarPath,
                installerPlan,
                sharedMinecraftDirectory,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                cancellationToken,
                downloadOperation).ConfigureAwait(false);
            await installerArtifactService.ValidatePublishedArtifactsAsync(
                sharedMinecraftDirectory,
                installerPlan,
                cancellationToken,
                downloadOperation).ConfigureAwait(false);
            logger.LogInformation(
                "Forge final version content installation completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} VersionName={VersionName} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                finalVersionName,
                finalContentStopwatch.ElapsedMilliseconds);

            // 最终版本完成扁平化、修复和文件补齐后才提交，用户目录不会看到依赖沙箱的半成品。
            var versionCopyStopwatch = Stopwatch.StartNew();
            LoaderVersionDirectoryTransaction.CopyFinalVersionDirectory(
                installerMinecraftDirectory,
                gameDirectory,
                finalVersionName,
                cancellationToken);
            logger.LogInformation(
                "Forge final version directory publication completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} VersionName={VersionName} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                finalVersionName,
                versionCopyStopwatch.ElapsedMilliseconds);
            var publishedValidationStopwatch = Stopwatch.StartNew();
            await installerArtifactService.ValidatePublishedArtifactsAsync(
                sharedMinecraftDirectory,
                installerPlan,
                cancellationToken,
                downloadOperation).ConfigureAwait(false);
            logger.LogInformation(
                "Forge published artifact validation completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} DurationMs={DurationMs}",
                minecraftVersion,
                selectedLoaderVersion,
                publishedValidationStopwatch.ElapsedMilliseconds);

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
