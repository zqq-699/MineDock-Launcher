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
internal sealed class ManagedVersionRepairService : IManagedVersionRepairService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly HttpClient httpClient;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;

    public ManagedVersionRepairService(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null)
    {
        this.httpClient = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger.Instance;
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
    internal async Task<ResolvedVersionMetadata> EnsureVersionIsSelfContainedAsync(
        string minecraftDirectory,
        string versionName,
        string versionDirectory,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        bool allowRepair = true,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        // 禁止修复时只验证现状，绝不写文件或下载；继承未消除即视为不可启动。
        if (!allowRepair)
        {
            var versionJson = await ReadVersionJsonAsync(versionDirectory, versionName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(GetStringProperty(versionJson, "inheritsFrom")))
            {
                throw new InstanceRepairException(
                    $"Version {versionName} still depends on another version and automatic repair is disabled.");
            }

            var jarPath = Path.Combine(versionDirectory, $"{versionName}.jar");
            return new ResolvedVersionMetadata(
                versionName,
                versionJson,
                File.Exists(jarPath) ? jarPath : null,
                VanillaVersionMetadataClient.GetClientJarUrl(versionJson),
                WasModified: false,
                ClientJarSha1: VanillaVersionMetadataClient.GetClientJarSha1(versionJson),
                ClientJarSize: VanillaVersionMetadataClient.GetClientJarSize(versionJson));
        }

        var result = await ResolveCurrentVersionAsync(
            minecraftDirectory,
            versionName,
            versionDirectory,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);

        if (!string.IsNullOrWhiteSpace(GetStringProperty(result.VersionJson, "inheritsFrom")))
            throw new InstanceRepairException($"Version {versionName} still depends on another version after repair.");

        // 修复结果统一改写为当前版本身份并移除 inheritsFrom，后续启动不再依赖父目录存在。
        var normalized = NormalizeVersionJson(result.VersionJson, versionName);
        if (result.WasModified || !ReferenceEquals(normalized, result.VersionJson))
        {
            await WriteVersionJsonAsync(versionDirectory, versionName, normalized, cancellationToken);
            result = result with { VersionJson = normalized, WasModified = true };
        }

        return result;
    }

    /// <summary>
    /// 读取当前版本；存在父版本时递归解析并合并为可独立使用的元数据。
    /// </summary>
    private async Task<ResolvedVersionMetadata> ResolveCurrentVersionAsync(
        string minecraftDirectory,
        string versionName,
        string versionDirectory,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond)
    {
        var versionJson = await ReadVersionJsonAsync(versionDirectory, versionName, cancellationToken);
        var currentJarPath = Path.Combine(versionDirectory, $"{versionName}.jar");
        var currentJarUrl = VanillaVersionMetadataClient.GetClientJarUrl(versionJson);
        var inheritsFrom = GetStringProperty(versionJson, "inheritsFrom");
        if (string.IsNullOrWhiteSpace(inheritsFrom))
        {
            return new ResolvedVersionMetadata(
                versionName,
                versionJson,
                File.Exists(currentJarPath) ? currentJarPath : null,
                currentJarUrl,
                WasModified: false,
                ClientJarSha1: VanillaVersionMetadataClient.GetClientJarSha1(versionJson),
                ClientJarSize: VanillaVersionMetadataClient.GetClientJarSize(versionJson));
        }

        var parent = await ResolveParentVersionAsync(
            minecraftDirectory,
            inheritsFrom,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
        var mergedVersion = VersionJsonMergeHelper.MergeFlattenedVersion(parent.VersionJson, versionJson, versionName);

        return new ResolvedVersionMetadata(
            versionName,
            mergedVersion,
            parent.LocalJarPath,
            VanillaVersionMetadataClient.GetClientJarUrl(mergedVersion) ?? parent.ClientJarUrl ?? currentJarUrl,
            WasModified: true,
            ClientJarSha1: VanillaVersionMetadataClient.GetClientJarSha1(mergedVersion) ?? parent.ClientJarSha1,
            ClientJarSize: VanillaVersionMetadataClient.GetClientJarSize(mergedVersion) ?? parent.ClientJarSize);
    }

    /// <summary>
    /// 优先读取本地父版本，缺失时从官方元数据获取父版本定义和客户端来源。
    /// </summary>
    private async Task<ResolvedVersionMetadata> ResolveParentVersionAsync(
        string minecraftDirectory,
        string parentVersionName,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond)
    {
        var parentDirectory = Path.Combine(minecraftDirectory, "versions", parentVersionName);
        if (Directory.Exists(parentDirectory)
            && File.Exists(Path.Combine(parentDirectory, $"{parentVersionName}.json")))
        {
            return await ResolveCurrentVersionAsync(
                minecraftDirectory,
                parentVersionName,
                parentDirectory,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
        }

        JsonObject remoteVersionJson;
        try
        {
            remoteVersionJson = await VanillaVersionMetadataClient.DownloadVersionJsonAsync(
                httpClient,
                parentVersionName,
                downloadSourcePreference,
                downloadSpeedLimitMbPerSecond,
                downloadSpeedLimitState,
                logger,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InstanceRepairException(
                $"Version {parentVersionName} metadata could not be repaired in place.",
                exception);
        }

        return new ResolvedVersionMetadata(
            parentVersionName,
            NormalizeVersionJson(remoteVersionJson, parentVersionName),
            LocalJarPath: null,
            VanillaVersionMetadataClient.GetClientJarUrl(remoteVersionJson),
            WasModified: false,
            ClientJarSha1: VanillaVersionMetadataClient.GetClientJarSha1(remoteVersionJson),
            ClientJarSize: VanillaVersionMetadataClient.GetClientJarSize(remoteVersionJson));
    }

    private static JsonObject NormalizeVersionJson(JsonObject versionJson, string versionName)
    {
        var normalized = (JsonObject)versionJson.DeepClone();
        normalized["id"] = versionName;
        normalized["jar"] = versionName;
        normalized.Remove("inheritsFrom");

        if (normalized["minecraftArguments"] is JsonValue minecraftArgumentsValue
            && minecraftArgumentsValue.TryGetValue<string>(out var minecraftArguments))
        {
            normalized["minecraftArguments"] = VersionJsonMergeHelper.NormalizeMinecraftArguments(minecraftArguments);
        }

        return normalized;
    }

    /// <summary>
    /// 确保隔离版本拥有同名客户端 JAR，优先复制本地来源并最后尝试下载。
    /// </summary>
    private async Task EnsureVersionJarAsync(
        string versionDirectory,
        string versionName,
        ResolvedVersionMetadata resolvedVersion,
        bool allowRepair,
        ManagedVersionRepairDownloadBatch downloadBatch,
        CancellationToken cancellationToken)
    {
        var jarPath = Path.Combine(versionDirectory, $"{versionName}.jar");
        if (File.Exists(jarPath))
            return;

        if (!allowRepair)
        {
            throw new InstanceRepairException(
                $"Version {versionName} is missing its client jar and automatic repair is disabled.");
        }

        if (!string.IsNullOrWhiteSpace(resolvedVersion.LocalJarPath)
            && File.Exists(resolvedVersion.LocalJarPath))
        {
            // 优先复用父版本或本地已有 JAR，只有无可用本地来源时才下载。
            File.Copy(resolvedVersion.LocalJarPath, jarPath, overwrite: false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(resolvedVersion.ClientJarUrl))
        {
            await downloadBatch.DownloadAsync(
                new RepairDownloadRequest(
                    OriginalUrl: resolvedVersion.ClientJarUrl,
                    jarPath,
                    ResourceCategory: "Mojang",
                    LibraryName: null,
                    ArtifactPath: $"{versionName}.jar",
                    ExpectedSha1: resolvedVersion.ClientJarSha1,
                    ExpectedSize: resolvedVersion.ClientJarSize),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InstanceRepairException($"Version {versionName} is missing its client jar and no repair source is available.");
    }

    /// <summary>
    /// 解析受当前平台规则允许的库构件，收集所有缺失项后批量下载。
    /// </summary>
    private async Task EnsureLibrariesAsync(
        string minecraftDirectory,
        JsonObject versionJson,
        bool allowRepair,
        ManagedVersionRepairDownloadBatch downloadBatch,
        CancellationToken cancellationToken)
    {
        if (versionJson["libraries"] is not JsonArray libraries)
            return;

        var downloads = new List<RepairDownloadRequest>();
        foreach (var libraryNode in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (libraryNode is not JsonObject library || !ManagedLibraryArtifactResolver.IsAllowed(library))
                continue;

            foreach (var artifact in ManagedLibraryArtifactResolver.EnumerateDownloads(library))
            {
                var destinationPath = Path.Combine(minecraftDirectory, "libraries", artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(destinationPath))
                    continue;

                if (!allowRepair)
                {
                    throw new InstanceRepairException(
                        $"Required library {artifact.RelativePath} is missing and automatic repair is disabled.");
                }

                downloads.Add(new RepairDownloadRequest(
                    OriginalUrl: artifact.Url,
                    destinationPath,
                    artifact.ResourceCategory,
                    artifact.LibraryName,
                    artifact.RelativePath,
                    artifact.Sha1,
                    artifact.Size));
            }
        }

        // 先收集缺失项再交给批次统一并发、限速和汇报进度。
        await downloadBatch.DownloadAllAsync(downloads, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 确保资源索引存在，并根据索引内容补齐缺失的对象哈希文件。
    /// </summary>
    private async Task EnsureAssetsAsync(
        string minecraftDirectory,
        JsonObject versionJson,
        bool allowRepair,
        ManagedVersionRepairDownloadBatch downloadBatch,
        CancellationToken cancellationToken)
    {
        if (versionJson["assetIndex"] is not JsonObject assetIndex)
            return;

        var assetIndexId = GetStringProperty(assetIndex, "id");
        var assetIndexUrl = GetStringProperty(assetIndex, "url");
        if (string.IsNullOrWhiteSpace(assetIndexId) || string.IsNullOrWhiteSpace(assetIndexUrl))
            return;

        var indexPath = Path.Combine(minecraftDirectory, "assets", "indexes", $"{assetIndexId}.json");
        if (!File.Exists(indexPath))
        {
            if (!allowRepair)
                throw new InstanceRepairException($"Asset index {assetIndexId} is missing and automatic repair is disabled.");

            await downloadBatch.DownloadAsync(
                new RepairDownloadRequest(
                    OriginalUrl: assetIndexUrl,
                    indexPath,
                    ResourceCategory: "Mojang",
                    LibraryName: null,
                    ArtifactPath: $"assets/indexes/{assetIndexId}.json",
                    ExpectedSha1: GetStringProperty(assetIndex, "sha1"),
                    ExpectedSize: GetLongProperty(assetIndex, "size")),
                cancellationToken).ConfigureAwait(false);
        }

        await using var indexStream = File.OpenRead(indexPath);
        var indexNode = await JsonNode.ParseAsync(indexStream, cancellationToken: cancellationToken)
            ?? throw new InstanceRepairException($"Asset index {assetIndexId} is empty.");
        if (indexNode["objects"] is not JsonObject objects)
            return;

        var downloads = new List<RepairDownloadRequest>();
        foreach (var asset in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (asset.Value is not JsonObject assetObject)
                continue;

            var hash = GetStringProperty(assetObject, "hash");
            if (hash.Length < 2)
                continue;

            var objectPath = Path.Combine(minecraftDirectory, "assets", "objects", hash[..2], hash);
            if (File.Exists(objectPath))
                continue;

            if (!allowRepair)
            {
                throw new InstanceRepairException(
                    $"Required asset {hash} is missing and automatic repair is disabled.");
            }

            var assetUrl = $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";
            downloads.Add(new RepairDownloadRequest(
                OriginalUrl: assetUrl,
                objectPath,
                ResourceCategory: "Mojang",
                LibraryName: null,
                ArtifactPath: $"assets/objects/{hash[..2]}/{hash}",
                ExpectedSha1: hash,
                ExpectedSize: GetLongProperty(assetObject, "size")));
        }

        await downloadBatch.DownloadAllAsync(downloads, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查版本声明的日志配置文件，并在允许修复时下载缺失配置。
    /// </summary>
    private async Task EnsureLoggingAsync(
        string minecraftDirectory,
        JsonObject versionJson,
        bool allowRepair,
        ManagedVersionRepairDownloadBatch downloadBatch,
        CancellationToken cancellationToken)
    {
        if (versionJson["logging"]?["client"] is not JsonObject clientLogging)
            return;

        if (clientLogging["file"] is not JsonObject loggingFile)
            return;

        var id = GetStringProperty(loggingFile, "id");
        var url = GetStringProperty(loggingFile, "url");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            return;

        var logConfigPath = Path.Combine(minecraftDirectory, "assets", "log_configs", id);
        if (File.Exists(logConfigPath))
            return;

        if (!allowRepair)
            throw new InstanceRepairException($"Logging configuration {id} is missing and automatic repair is disabled.");

        await downloadBatch.DownloadAsync(
            new RepairDownloadRequest(
                OriginalUrl: url,
                logConfigPath,
                ResourceCategory: "Mojang",
                LibraryName: null,
                ArtifactPath: id,
                ExpectedSha1: GetStringProperty(loggingFile, "sha1"),
                ExpectedSize: GetLongProperty(loggingFile, "size")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonObject> ReadVersionJsonAsync(
        string versionDirectory,
        string versionName,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        if (!File.Exists(jsonPath))
            throw new InstanceRepairException($"Version metadata is missing for {versionName}.");

        await using var stream = File.OpenRead(jsonPath);
        var jsonNode = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InstanceRepairException($"Version metadata is empty for {versionName}.");
        return jsonNode.AsObject();
    }

    private static Task WriteVersionJsonAsync(
        string versionDirectory,
        string versionName,
        JsonObject versionJson,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        return File.WriteAllTextAsync(
            jsonPath,
            versionJson.ToJsonString(JsonOptions),
            cancellationToken);
    }

    private static string ResolveVersionDirectory(string minecraftDirectory, string versionName, string instanceDirectory)
    {
        var expectedDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        if (string.IsNullOrWhiteSpace(instanceDirectory))
            return expectedDirectory;

        var normalizedExpected = Path.GetFullPath(expectedDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedInstance = Path.GetFullPath(instanceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return PathComparer.Equals(normalizedExpected, normalizedInstance)
            ? expectedDirectory
            : normalizedInstance;
    }

    private static string GetStringProperty(JsonObject node, string propertyName)
    {
        return node[propertyName]?.GetValue<string>() ?? string.Empty;
    }

    private static long? GetLongProperty(JsonObject node, string propertyName)
    {
        return node[propertyName]?.GetValue<long?>();
    }

    private static void ReportProgress(
        IProgress<LauncherProgress>? progress,
        string stage,
        string message,
        double percent)
    {
        progress?.Report(new LauncherProgress(stage, message, percent));
    }

    internal sealed record ResolvedVersionMetadata(
        string VersionName,
        JsonObject VersionJson,
        string? LocalJarPath,
        string? ClientJarUrl,
        bool WasModified,
        string? ClientJarSha1 = null,
        long? ClientJarSize = null);

}
