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
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal interface IManagedVersionRepairService
{
    Task RepairAsync(
        string minecraftDirectory,
        string versionName,
        string instanceDirectory,
        IProgress<LauncherProgress>? progress,
        bool allowRepair,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0);
}

/// <summary>
/// 检查或修复版本 JSON、客户端 JAR、库、资源和日志配置，并将继承版本规范化为自包含版本。
/// </summary>
internal sealed partial class ManagedVersionRepairService : IManagedVersionRepairService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;
    private readonly LoaderInstallerArtifactService loaderInstallerArtifactService;

    public ManagedVersionRepairService(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null,
        IForgeInstallerRunner? forgeInstallerRunner = null,
        IFinalVersionInstaller? finalVersionInstaller = null,
        string? tempRootDirectory = null)
    {
        this.httpClient = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger.Instance;
        loaderInstallerArtifactService = new LoaderInstallerArtifactService(
            this.httpClient,
            forgeInstallerRunner,
            finalVersionInstaller,
            downloadSpeedLimitState,
            this.logger,
            tempRootDirectory);
    }

    /// <summary>
    /// 按元数据、JAR、库、资源和日志配置的顺序检查或修复启动所需文件。
    /// </summary>
    public async Task RepairAsync(
        string minecraftDirectory,
        string versionName,
        string instanceDirectory,
        IProgress<LauncherProgress>? progress,
        bool allowRepair,
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var versionDirectory = ResolveVersionDirectory(minecraftDirectory, versionName, instanceDirectory);
        if (!Directory.Exists(versionDirectory))
            throw new InstanceRepairException($"Version directory is missing for {versionName}.");

        var downloadBatch = new ManagedVersionRepairDownloadBatch(
            httpClient,
            downloadSpeedLimitState,
            logger,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            progress);
        ReportProgress(progress, LaunchProgressStages.CheckingInstance, "Checking instance files", 6);
        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingMetadata : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing version metadata" : "Checking launch files",
            18);
        var resolvedVersion = await EnsureVersionIsSelfContainedAsync(
            minecraftDirectory,
            versionName,
            versionDirectory,
            downloadSourcePreference,
            cancellationToken,
            allowRepair,
            downloadSpeedLimitMbPerSecond);

        var versionJsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        if (LoaderInstallerArtifactService.HasLegacyManifest(resolvedVersion.VersionJson, "forgeProcessorArtifacts"))
        {
            if (!LoaderInstallerArtifactService.TryResolveForgeIdentity(resolvedVersion.VersionJson, out var minecraftVersion, out var forgeVersion))
                throw new InstanceRepairException("Forge version metadata is incomplete and cannot be migrated.");
            var migrated = await loaderInstallerArtifactService.RepairLegacyVersionAsync(
                minecraftDirectory,
                versionJsonPath,
                resolvedVersion.VersionJson,
                minecraftVersion,
                forgeVersion,
                "forgeProcessorArtifacts",
                "Forge",
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{minecraftVersion}-{forgeVersion}/forge-{minecraftVersion}-{forgeVersion}-installer.jar",
                progress,
                allowRepair,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                cancellationToken).ConfigureAwait(false);
            resolvedVersion = resolvedVersion with { VersionJson = migrated, WasModified = true };
        }
        if (LoaderInstallerArtifactService.HasLegacyManifest(resolvedVersion.VersionJson, "neoForgeProcessorArtifacts"))
        {
            if (!LoaderInstallerArtifactService.TryResolveNeoForgeIdentity(resolvedVersion.VersionJson, out var minecraftVersion, out var neoForgeVersion))
                throw new InstanceRepairException("NeoForge version metadata is incomplete and cannot be migrated.");
            var migrated = await loaderInstallerArtifactService.RepairLegacyVersionAsync(
                minecraftDirectory,
                versionJsonPath,
                resolvedVersion.VersionJson,
                minecraftVersion,
                neoForgeVersion,
                "neoForgeProcessorArtifacts",
                "NeoForge",
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoForgeVersion}/neoforge-{neoForgeVersion}-installer.jar",
                progress,
                allowRepair,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                cancellationToken).ConfigureAwait(false);
            resolvedVersion = resolvedVersion with { VersionJson = migrated, WasModified = true };
        }

        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingJar : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing instance jar" : "Checking launch files",
            32);
        await EnsureVersionJarAsync(
            versionDirectory,
            versionName,
            resolvedVersion,
            allowRepair,
            downloadBatch,
            cancellationToken);

        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingLibraries : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing shared libraries" : "Checking launch files",
            48);
        await EnsureLibrariesAsync(
            minecraftDirectory,
            resolvedVersion.VersionJson,
            allowRepair,
            downloadBatch,
            cancellationToken);

        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingAssets : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing shared assets" : "Checking launch files",
            64);
        await EnsureAssetsAsync(
            minecraftDirectory,
            resolvedVersion.VersionJson,
            allowRepair,
            downloadBatch,
            cancellationToken);

        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingLogging : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing logging configuration" : "Checking launch files",
            80);
        await EnsureLoggingAsync(
            minecraftDirectory,
            resolvedVersion.VersionJson,
            allowRepair,
            downloadBatch,
            cancellationToken);

        ReportProgress(progress, LaunchProgressStages.CheckingJava, "Checking Java runtime", 90);
    }

    /// <summary>
    /// 验证或消除 inheritsFrom 依赖，并返回最终版本 JSON 与客户端 JAR 来源。
    /// </summary>
}
