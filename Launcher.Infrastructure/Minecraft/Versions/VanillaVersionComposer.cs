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

namespace Launcher.Infrastructure.Minecraft;

internal static class VanillaVersionComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<string> CreateFinalVersionAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string finalVersionName,
        string minecraftDirectory,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        MinecraftDownloadOperationContext? operationContext = null,
        SlidingWindowDownloadSpeedReporter? speedReporter = null)
    {
        var prepared = await PrepareFinalVersionAsync(
            httpClient,
            minecraftVersion,
            finalVersionName,
            minecraftDirectory,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken,
            operationContext,
            speedReporter).ConfigureAwait(false);
        try
        {
            await prepared.ClientJarDownload.ConfigureAwait(false);
            return prepared.VersionName;
        }
        catch
        {
            await prepared.CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<PreparedVersionInstall> PrepareFinalVersionAsync(
        HttpClient httpClient,
        string minecraftVersion,
        string finalVersionName,
        string minecraftDirectory,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond = 0,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        MinecraftDownloadOperationContext? operationContext = null,
        SlidingWindowDownloadSpeedReporter? speedReporter = null)
    {
        var finalVersionDirectory = Path.Combine(minecraftDirectory, "versions", finalVersionName);
        var finalVersionJsonPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.json");
        var finalVersionJarPath = Path.Combine(finalVersionDirectory, $"{finalVersionName}.jar");

        if (Directory.Exists(finalVersionDirectory))
            throw new IOException($"Version directory already exists: {finalVersionName}");

        var baseVersionJson = await DownloadBaseVersionJsonAsync(
            httpClient,
            minecraftVersion,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);
        var finalVersionJson = BuildFinalVersionJson(baseVersionJson, finalVersionName, minecraftVersion);

        Directory.CreateDirectory(finalVersionDirectory);

        try
        {
            await File.WriteAllTextAsync(
                finalVersionJsonPath,
                finalVersionJson.ToJsonString(JsonOptions),
                cancellationToken);
            var clientJarDownload = DownloadClientJarAsync(
                httpClient,
                baseVersionJson,
                finalVersionJarPath,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken,
                operationContext,
                speedReporter);
            return new PreparedVersionInstall(finalVersionName, finalVersionDirectory, clientJarDownload);
        }
        catch
        {
            if (Directory.Exists(finalVersionDirectory))
                Directory.Delete(finalVersionDirectory, recursive: true);
            throw;
        }
    }

    internal static JsonObject BuildFinalVersionJson(
        JsonObject baseVersionJson,
        string finalVersionName,
        string minecraftVersion)
    {
        var finalVersionJson = (JsonObject)baseVersionJson.DeepClone();
        finalVersionJson["id"] = finalVersionName;
        finalVersionJson["jar"] = finalVersionName;
        finalVersionJson.Remove("inheritsFrom");
        LauncherVersionMetadata.Apply(finalVersionJson, minecraftVersion);
        return finalVersionJson;
    }

    private static async Task<JsonObject> DownloadBaseVersionJsonAsync(
        HttpClient httpClient,
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        return await VanillaVersionMetadataClient.DownloadVersionJsonAsync(
            httpClient,
            minecraftVersion,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond,
            downloadSpeedLimitState,
            logger,
            cancellationToken);
    }

    private static async Task DownloadClientJarAsync(
        HttpClient httpClient,
        JsonObject baseVersionJson,
        string destinationJarPath,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger? logger,
        CancellationToken cancellationToken,
        MinecraftDownloadOperationContext? operationContext,
        SlidingWindowDownloadSpeedReporter? speedReporter)
    {
        var clientUrl = VanillaVersionMetadataClient.GetClientJarUrl(baseVersionJson);
        if (string.IsNullOrWhiteSpace(clientUrl))
            throw new InvalidDataException("Minecraft version metadata is missing downloads.client.url.");

        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Runtime);
        var sha1 = VanillaVersionMetadataClient.GetClientJarSha1(baseVersionJson);
        using var speedSession = speedReporter is null ? null : new DownloadActivitySpeedSession(speedReporter);
        await executor.DownloadFileAsync(
            clientUrl,
            downloadSourcePreference,
            categoryHint: "Mojang",
            destinationJarPath,
            sha1,
            VanillaVersionMetadataClient.GetClientJarSize(baseVersionJson),
            reportDownloadedBytes: speedReporter is null ? null : bytes => speedReporter.ReportNetworkBytes(bytes),
            cancellationToken,
            reportActivity: speedSession is null ? null : activity => speedSession.Report(activity),
            options: operationContext is not null && MinecraftFileIntegrity.IsSha1(sha1)
                ? new DownloadFileOptions(DownloadPersistenceMode.TaskScopedResumable, operationContext)
                : null);
    }
}
