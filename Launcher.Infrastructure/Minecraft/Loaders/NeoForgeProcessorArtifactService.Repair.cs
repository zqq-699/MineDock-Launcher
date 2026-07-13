/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal sealed partial class NeoForgeProcessorArtifactService
{
    public async Task EnsureLaunchArtifactsAsync(
        string minecraftDirectory,
        string versionJsonPath,
        JsonObject versionJson,
        bool allowRepair,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        var persistedManifest = ReadManifest(versionJson);
        if (persistedManifest is not null)
        {
            try
            {
                await ValidateManifestAsync(minecraftDirectory, persistedManifest, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (InstanceRepairException) when (allowRepair)
            {
                logger.LogWarning(
                    "NeoForge installer artifacts failed integrity validation and will be repaired. MinecraftVersion={MinecraftVersion} NeoForgeVersion={NeoForgeVersion}",
                    persistedManifest.MinecraftVersion,
                    persistedManifest.NeoForgeVersion);
            }
        }

        if (!TryResolveNeoForgeIdentity(versionJson, out var minecraftVersion, out var neoForgeVersion))
            return;

        var repairKey = $"{Path.GetFullPath(minecraftDirectory)}|{minecraftVersion}|{neoForgeVersion}";
        var repairLock = RepairLocks.GetOrAdd(repairKey, static _ => new SemaphoreSlim(1, 1));
        await repairLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentVersionJson = await ReadVersionJsonAsync(versionJsonPath, cancellationToken).ConfigureAwait(false);
            persistedManifest = ReadManifest(currentVersionJson);
            if (persistedManifest is not null)
            {
                try
                {
                    await ValidateManifestAsync(minecraftDirectory, persistedManifest, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (InstanceRepairException) when (allowRepair)
                {
                }
            }

            var sessionDirectory = Path.Combine(tempRootDirectory, "launcher-neoforge-repair", Guid.NewGuid().ToString("N"));
            var installerJarPath = Path.Combine(sessionDirectory, $"neoforge-{neoForgeVersion}-installer.jar");
            var installerMinecraftDirectory = Path.Combine(sessionDirectory, ".minecraft");
            Directory.CreateDirectory(sessionDirectory);

            try
            {
                await DownloadInstallerAsync(
                    installerJarPath,
                    neoForgeVersion,
                    downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond,
                    cancellationToken).ConfigureAwait(false);

                if (!allowRepair)
                {
                    var expectedArtifacts = await ReadExpectedArtifactsAsync(installerJarPath, cancellationToken)
                        .ConfigureAwait(false);
                    await ValidateExpectedArtifactsAsync(
                        minecraftDirectory,
                        expectedArtifacts,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                LoaderVersionDirectoryTransaction.EnsureLauncherProfileExists(installerMinecraftDirectory);
                Directory.CreateDirectory(Path.Combine(installerMinecraftDirectory, "versions"));
                var prerequisiteSeeder = new LoaderInstallerPrerequisiteSeeder(logger);
                await prerequisiteSeeder.SeedAsync(
                    minecraftDirectory,
                    installerMinecraftDirectory,
                    minecraftVersion,
                    installerJarPath,
                    cancellationToken).ConfigureAwait(false);

                await EnsureVanillaVersionAsync(
                    installerMinecraftDirectory,
                    minecraftVersion,
                    downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond,
                    cancellationToken).ConfigureAwait(false);

                await installerRunner.RunInstallerAsync(
                    "java",
                    installerJarPath,
                    installerMinecraftDirectory,
                    cancellationToken).ConfigureAwait(false);

                var repairedManifest = await ValidateInstallerOutputsAsync(
                    installerJarPath,
                    installerMinecraftDirectory,
                    minecraftVersion,
                    neoForgeVersion,
                    cancellationToken).ConfigureAwait(false);

                foreach (var artifact in repairedManifest.Artifacts)
                {
                    var sourcePath = ResolveLibraryPath(installerMinecraftDirectory, artifact.RelativePath);
                    var destinationPath = ResolveLibraryPath(minecraftDirectory, artifact.RelativePath);
                    await AtomicSharedFilePublisher.PublishVerifiedReplacementAsync(
                        sourcePath,
                        destinationPath,
                        artifact.Sha1,
                        cancellationToken).ConfigureAwait(false);
                }

                await ValidateManifestAsync(minecraftDirectory, repairedManifest, cancellationToken).ConfigureAwait(false);
                ApplyManifest(currentVersionJson, repairedManifest);
                await AtomicJsonFileWriter.WriteAsync(
                    versionJsonPath,
                    currentVersionJson,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Repaired NeoForge installer artifacts. MinecraftVersion={MinecraftVersion} NeoForgeVersion={NeoForgeVersion} ArtifactCount={ArtifactCount}",
                    minecraftVersion,
                    neoForgeVersion,
                    repairedManifest.Artifacts.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InstanceRepairException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InstanceRepairException(
                    $"NeoForge {neoForgeVersion} processor outputs could not be repaired.",
                    exception);
            }
            finally
            {
                LoaderVersionDirectoryTransaction.TryDeleteDirectory(sessionDirectory);
            }
        }
        finally
        {
            repairLock.Release();
        }
    }

    private static async Task<JsonObject> ReadVersionJsonAsync(
        string versionJsonPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            versionJsonPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject
            ?? throw new InstanceRepairException($"Version metadata is empty: {versionJsonPath}");
    }

    private async Task DownloadInstallerAsync(
        string installerJarPath,
        string neoForgeVersion,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        var url = $"{InstallerBaseUrl}/{neoForgeVersion}/neoforge-{neoForgeVersion}-installer.jar";
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Runtime);
        await executor.DownloadFileAsync(
            url,
            downloadSourcePreference,
            "NeoForge",
            installerJarPath,
            expectedSha1: null,
            expectedSize: null,
            reportDownloadedBytes: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureVanillaVersionAsync(
        string installerMinecraftDirectory,
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        var versionDirectory = Path.Combine(installerMinecraftDirectory, "versions", minecraftVersion);
        if (File.Exists(Path.Combine(versionDirectory, $"{minecraftVersion}.json"))
            && File.Exists(Path.Combine(versionDirectory, $"{minecraftVersion}.jar")))
        {
            return;
        }

        await finalVersionInstaller.InstallAsync(
            installerMinecraftDirectory,
            minecraftVersion,
            downloadSourcePreference,
            progress: null,
            cancellationToken,
            downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
    }

    private static async Task ValidateExpectedArtifactsAsync(
        string minecraftDirectory,
        IReadOnlyList<NeoForgeProcessorExpectedArtifact> expectedArtifacts,
        CancellationToken cancellationToken)
    {
        if (expectedArtifacts.Count == 0)
            throw new InstanceRepairException("NeoForge installer did not declare any client processor artifacts.");

        foreach (var artifact in expectedArtifacts)
        {
            var path = ResolveLibraryPath(minecraftDirectory, artifact.RelativePath);
            var status = await MinecraftFileIntegrity.EvaluateAsync(
                path,
                artifact.TrustedSha1,
                expectedSize: null,
                MinecraftFileVerification.Full,
                cancellationToken).ConfigureAwait(false);
            if (status is not MinecraftFileIntegrityStatus.Valid)
            {
                throw new InstanceRepairException(
                    $"NeoForge processor output is missing or invalid ({status}) and automatic repair is disabled: {artifact.RelativePath}");
            }
        }
    }
}
