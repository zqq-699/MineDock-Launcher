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

internal sealed partial class ManagedVersionRepairService
{
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
}
