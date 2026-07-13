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
}
