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
using System.Net;
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

internal sealed class ManagedVersionRepairService : IManagedVersionRepairService
{
    private const int MaxRepairDownloadConcurrency = 8;
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

        var bandwidthLimiter = DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState);
        var downloadSpeedReporter = new RepairDownloadSpeedReporter(progress);
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
            downloadSourcePreference,
            allowRepair,
            downloadSpeedReporter,
            cancellationToken,
            downloadSpeedLimitMbPerSecond,
            bandwidthLimiter);

        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingLibraries : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing shared libraries" : "Checking launch files",
            48);
        await EnsureLibrariesAsync(
            minecraftDirectory,
            resolvedVersion.VersionJson,
            downloadSourcePreference,
            allowRepair,
            downloadSpeedReporter,
            cancellationToken,
            downloadSpeedLimitMbPerSecond,
            bandwidthLimiter);

        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingAssets : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing shared assets" : "Checking launch files",
            64);
        await EnsureAssetsAsync(
            minecraftDirectory,
            resolvedVersion.VersionJson,
            downloadSourcePreference,
            allowRepair,
            downloadSpeedReporter,
            cancellationToken,
            downloadSpeedLimitMbPerSecond,
            bandwidthLimiter);

        ReportProgress(
            progress,
            allowRepair ? LaunchProgressStages.RepairingLogging : LaunchProgressStages.CheckingFiles,
            allowRepair ? "Repairing logging configuration" : "Checking launch files",
            80);
        await EnsureLoggingAsync(
            minecraftDirectory,
            resolvedVersion.VersionJson,
            downloadSourcePreference,
            allowRepair,
            downloadSpeedReporter,
            cancellationToken,
            downloadSpeedLimitMbPerSecond,
            bandwidthLimiter);

        ReportProgress(progress, LaunchProgressStages.CheckingJava, "Checking Java runtime", 90);
    }

    internal async Task<ResolvedVersionMetadata> EnsureVersionIsSelfContainedAsync(
        string minecraftDirectory,
        string versionName,
        string versionDirectory,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        bool allowRepair = true,
        int downloadSpeedLimitMbPerSecond = 0)
    {
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

        var normalized = NormalizeVersionJson(result.VersionJson, versionName);
        if (result.WasModified || !ReferenceEquals(normalized, result.VersionJson))
        {
            await WriteVersionJsonAsync(versionDirectory, versionName, normalized, cancellationToken);
            result = result with { VersionJson = normalized, WasModified = true };
        }

        return result;
    }

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

    private async Task EnsureVersionJarAsync(
        string versionDirectory,
        string versionName,
        ResolvedVersionMetadata resolvedVersion,
        DownloadSourcePreference downloadSourcePreference,
        bool allowRepair,
        RepairDownloadSpeedReporter downloadSpeedReporter,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond,
        DownloadBandwidthLimiter? bandwidthLimiter)
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
            File.Copy(resolvedVersion.LocalJarPath, jarPath, overwrite: false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(resolvedVersion.ClientJarUrl))
        {
            await DownloadFileAsync(
                new DownloadRequest(
                    OriginalUrl: resolvedVersion.ClientJarUrl,
                    jarPath,
                    ResourceCategory: "Mojang",
                    LibraryName: null,
                    ArtifactPath: $"{versionName}.jar",
                    ExpectedSha1: resolvedVersion.ClientJarSha1,
                    ExpectedSize: resolvedVersion.ClientJarSize),
                downloadSourcePreference,
                downloadSpeedReporter,
                cancellationToken,
                downloadSpeedLimitMbPerSecond,
                bandwidthLimiter);
            return;
        }

        throw new InstanceRepairException($"Version {versionName} is missing its client jar and no repair source is available.");
    }

    private async Task EnsureLibrariesAsync(
        string minecraftDirectory,
        JsonObject versionJson,
        DownloadSourcePreference downloadSourcePreference,
        bool allowRepair,
        RepairDownloadSpeedReporter downloadSpeedReporter,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond,
        DownloadBandwidthLimiter? bandwidthLimiter)
    {
        if (versionJson["libraries"] is not JsonArray libraries)
            return;

        var downloads = new List<DownloadRequest>();
        foreach (var libraryNode in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (libraryNode is not JsonObject library || !IsLibraryAllowed(library))
                continue;

            foreach (var artifact in EnumerateLibraryDownloads(library))
            {
                var destinationPath = Path.Combine(minecraftDirectory, "libraries", artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(destinationPath))
                    continue;

                if (!allowRepair)
                {
                    throw new InstanceRepairException(
                        $"Required library {artifact.RelativePath} is missing and automatic repair is disabled.");
                }

                downloads.Add(new DownloadRequest(
                    OriginalUrl: artifact.Url,
                    destinationPath,
                    artifact.ResourceCategory,
                    artifact.LibraryName,
                    artifact.RelativePath,
                    artifact.Sha1,
                    artifact.Size));
            }
        }

        await DownloadFilesAsync(
            downloads,
            downloadSourcePreference,
            downloadSpeedReporter,
            cancellationToken,
            downloadSpeedLimitMbPerSecond,
            bandwidthLimiter);
    }

    private async Task EnsureAssetsAsync(
        string minecraftDirectory,
        JsonObject versionJson,
        DownloadSourcePreference downloadSourcePreference,
        bool allowRepair,
        RepairDownloadSpeedReporter downloadSpeedReporter,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond,
        DownloadBandwidthLimiter? bandwidthLimiter)
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

            await DownloadFileAsync(
                new DownloadRequest(
                    OriginalUrl: assetIndexUrl,
                    indexPath,
                    ResourceCategory: "Mojang",
                    LibraryName: null,
                    ArtifactPath: $"assets/indexes/{assetIndexId}.json",
                    ExpectedSha1: GetStringProperty(assetIndex, "sha1"),
                    ExpectedSize: GetLongProperty(assetIndex, "size")),
                downloadSourcePreference,
                downloadSpeedReporter,
                cancellationToken,
                downloadSpeedLimitMbPerSecond,
                bandwidthLimiter);
        }

        await using var indexStream = File.OpenRead(indexPath);
        var indexNode = await JsonNode.ParseAsync(indexStream, cancellationToken: cancellationToken)
            ?? throw new InstanceRepairException($"Asset index {assetIndexId} is empty.");
        if (indexNode["objects"] is not JsonObject objects)
            return;

        var downloads = new List<DownloadRequest>();
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
            downloads.Add(new DownloadRequest(
                OriginalUrl: assetUrl,
                objectPath,
                ResourceCategory: "Mojang",
                LibraryName: null,
                ArtifactPath: $"assets/objects/{hash[..2]}/{hash}",
                ExpectedSha1: hash,
                ExpectedSize: GetLongProperty(assetObject, "size")));
        }

        await DownloadFilesAsync(
            downloads,
            downloadSourcePreference,
            downloadSpeedReporter,
            cancellationToken,
            downloadSpeedLimitMbPerSecond,
            bandwidthLimiter);
    }

    private async Task EnsureLoggingAsync(
        string minecraftDirectory,
        JsonObject versionJson,
        DownloadSourcePreference downloadSourcePreference,
        bool allowRepair,
        RepairDownloadSpeedReporter downloadSpeedReporter,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond,
        DownloadBandwidthLimiter? bandwidthLimiter)
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

        await DownloadFileAsync(
            new DownloadRequest(
                OriginalUrl: url,
                logConfigPath,
                ResourceCategory: "Mojang",
                LibraryName: null,
                ArtifactPath: id,
                ExpectedSha1: GetStringProperty(loggingFile, "sha1"),
                ExpectedSize: GetLongProperty(loggingFile, "size")),
            downloadSourcePreference,
            downloadSpeedReporter,
            cancellationToken,
            downloadSpeedLimitMbPerSecond,
            bandwidthLimiter);
    }

    private IEnumerable<LibraryArtifact> EnumerateLibraryDownloads(JsonObject library)
    {
        if (library["downloads"] is JsonObject downloads)
        {
            if (downloads["artifact"] is JsonObject artifact)
            {
                var resolved = TryCreateLibraryArtifact(library, artifact);
                if (resolved is not null)
                    yield return resolved;
            }

            if (downloads["classifiers"] is JsonObject classifiers)
            {
                var classifierKey = ResolveNativeClassifierKey(library);
                if (!string.IsNullOrWhiteSpace(classifierKey)
                    && classifiers[classifierKey] is JsonObject classifierArtifact)
                {
                    var resolved = TryCreateLibraryArtifact(library, classifierArtifact, classifierKey);
                    if (resolved is not null)
                        yield return resolved;
                    yield break;
                }
            }
        }

        if (library["downloads"] is null)
        {
            var resolved = TryCreateLibraryArtifactFromName(library, classifier: null);
            if (resolved is not null)
                yield return resolved;

            var classifierKey = ResolveNativeClassifierKey(library);
            if (!string.IsNullOrWhiteSpace(classifierKey))
            {
                var classifierArtifact = TryCreateLibraryArtifactFromName(library, classifierKey);
                if (classifierArtifact is not null)
                    yield return classifierArtifact;
            }
        }
    }

    private LibraryArtifact? TryCreateLibraryArtifact(JsonObject library, JsonObject artifact, string? classifier = null)
    {
        var libraryName = GetStringProperty(library, "name");
        var url = GetStringProperty(artifact, "url");
        var relativePath = GetStringProperty(artifact, "path");
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = TryCreateLibraryArtifactFromName(library, classifier)?.RelativePath ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (string.IsNullOrWhiteSpace(url))
        {
            url = TryResolveLibraryUrl(library, relativePath);
        }

        if (string.IsNullOrWhiteSpace(url))
            return null;

        return new LibraryArtifact(
            url,
            relativePath,
            string.IsNullOrWhiteSpace(libraryName) ? null : libraryName,
            ResolveResourceCategory(url),
            GetStringProperty(artifact, "sha1"),
            GetLongProperty(artifact, "size"));
    }

    private LibraryArtifact? TryCreateLibraryArtifactFromName(JsonObject library, string? classifier)
    {
        var name = GetStringProperty(library, "name");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (!TryBuildMavenPath(name, classifier, out var relativePath))
            return null;

        var baseUrl = ResolveLibraryBaseUrl(library);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        return new LibraryArtifact(
            new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath).AbsoluteUri,
            relativePath,
            name,
            ResolveResourceCategory(baseUrl));
    }

    private static string? TryResolveLibraryUrl(JsonObject library, string relativePath)
    {
        var baseUrl = ResolveLibraryBaseUrl(library);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        return new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath).AbsoluteUri;
    }

    private static bool TryBuildMavenPath(string mavenName, string? classifierOverride, out string relativePath)
    {
        relativePath = string.Empty;
        var parts = mavenName.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || parts.Length > 4)
            return false;

        var extension = "jar";
        var versionPart = parts[2];
        var versionAndExtension = versionPart.Split('@', 2, StringSplitOptions.TrimEntries);
        var version = versionAndExtension[0];
        if (versionAndExtension.Length == 2 && !string.IsNullOrWhiteSpace(versionAndExtension[1]))
            extension = versionAndExtension[1];

        var classifier = classifierOverride;
        if (string.IsNullOrWhiteSpace(classifier) && parts.Length == 4)
        {
            var classifierAndExtension = parts[3].Split('@', 2, StringSplitOptions.TrimEntries);
            classifier = classifierAndExtension[0];
            if (classifierAndExtension.Length == 2 && !string.IsNullOrWhiteSpace(classifierAndExtension[1]))
                extension = classifierAndExtension[1];
        }

        var groupPath = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var fileName = string.IsNullOrWhiteSpace(classifier)
            ? $"{artifact}-{version}.{extension}"
            : $"{artifact}-{version}-{classifier}.{extension}";

        relativePath = $"{groupPath}/{artifact}/{version}/{fileName}";
        return true;
    }

    private static string? ResolveLibraryBaseUrl(JsonObject library)
    {
        var baseUrl = GetStringProperty(library, "url");
        if (!string.IsNullOrWhiteSpace(baseUrl))
            return EnsureTrailingSlash(baseUrl);

        var name = GetStringProperty(library, "name");
        if (name.StartsWith("net.minecraftforge:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.minecraftforge.net/";

        if (name.StartsWith("net.fabricmc:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.fabricmc.net/";

        if (name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.neoforged.net/releases/";

        if (name.StartsWith("org.quiltmc:", StringComparison.OrdinalIgnoreCase))
            return "https://maven.quiltmc.org/repository/release/";

        return "https://libraries.minecraft.net/";
    }

    private static string EnsureTrailingSlash(string baseUrl)
    {
        return baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : $"{baseUrl}/";
    }

    private static string ResolveResourceCategory(string url, string? categoryHint = null)
    {
        return MinecraftDownloadSourceResolver.ResolveRequest(
            url,
            DownloadSourcePreference.Official,
            useBmclApi: false,
            categoryHint).ResourceCategory;
    }

    private static string? ResolveNativeClassifierKey(JsonObject library)
    {
        if (library["natives"] is not JsonObject natives)
            return null;

        var osName = GetCurrentOsName();
        var classifier = GetStringProperty(natives, osName);
        if (string.IsNullOrWhiteSpace(classifier))
            return null;

        return classifier.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
    }

    private static bool IsLibraryAllowed(JsonObject library)
    {
        if (library["rules"] is not JsonArray rules || rules.Count == 0)
            return true;

        var allowed = false;
        foreach (var ruleNode in rules)
        {
            if (ruleNode is not JsonObject rule || !DoesRuleMatch(rule))
                continue;

            var action = GetStringProperty(rule, "action");
            allowed = !string.Equals(action, "disallow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static bool DoesRuleMatch(JsonObject rule)
    {
        if (rule["features"] is JsonObject)
            return false;

        if (rule["os"] is not JsonObject os)
            return true;

        var name = GetStringProperty(os, "name");
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, GetCurrentOsName(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var arch = GetStringProperty(os, "arch");
        if (!string.IsNullOrWhiteSpace(arch))
        {
            if (Environment.Is64BitOperatingSystem && !arch.Contains("64", StringComparison.Ordinal))
                return false;

            if (!Environment.Is64BitOperatingSystem && arch.Contains("64", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private async Task DownloadFilesAsync(
        IEnumerable<DownloadRequest> downloads,
        DownloadSourcePreference downloadSourcePreference,
        RepairDownloadSpeedReporter downloadSpeedReporter,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond,
        DownloadBandwidthLimiter? bandwidthLimiter)
    {
        var uniqueDownloads = downloads
            .Where(download => !string.IsNullOrWhiteSpace(download.OriginalUrl)
                && !string.IsNullOrWhiteSpace(download.DestinationPath))
            .GroupBy(download => Path.GetFullPath(download.DestinationPath), PathComparer)
            .Select(group => group.First())
            .ToList();
        if (uniqueDownloads.Count == 0)
            return;

        await Parallel.ForEachAsync(
            uniqueDownloads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxRepairDownloadConcurrency,
                CancellationToken = cancellationToken
            },
            async (download, token) =>
            {
                await DownloadFileAsync(
                    download,
                    downloadSourcePreference,
                    downloadSpeedReporter,
                    token,
                    downloadSpeedLimitMbPerSecond,
                    bandwidthLimiter);
            });
    }

    private async Task DownloadFileAsync(
        DownloadRequest download,
        DownloadSourcePreference downloadSourcePreference,
        RepairDownloadSpeedReporter downloadSpeedReporter,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond,
        DownloadBandwidthLimiter? bandwidthLimiter)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(download.DestinationPath)!);
            downloadSpeedReporter.ReportDownloadStarted();
            var executor = new MinecraftDownloadRequestExecutor(
                httpClient,
                logger,
                bandwidthLimiter ?? DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
                category: DownloadConcurrencyCategory.Runtime);
            await executor.DownloadFileAsync(
                download.OriginalUrl,
                downloadSourcePreference,
                download.ResourceCategory,
                download.DestinationPath,
                download.ExpectedSha1,
                download.ExpectedSize,
                downloadSpeedReporter.ReportDownloadedBytes,
                cancellationToken);
        }
        catch (MinecraftDownloadRequestExecutor.DownloadSourceRequestException exception)
        {
            var statusCode = exception.Failures
                .OfType<DownloadAttemptException>()
                .LastOrDefault(failure => failure.StatusCode is not null)?
                .StatusCode;
            throw CreateDownloadException(download, exception.Resolution, statusCode, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InstanceRepairException)
        {
            var resolution = MinecraftDownloadSourceResolver
                .EnumerateRequests(
                    download.OriginalUrl,
                    downloadSourcePreference,
                    download.ResourceCategory)
                .First();
            throw CreateDownloadException(download, resolution, statusCode: null, exception);
        }
    }

    private static InstanceRepairException CreateDownloadException(
        DownloadRequest download,
        ResolvedDownloadRequest resolution,
        HttpStatusCode? statusCode,
        Exception? innerException)
    {
        var diagnostic = new LaunchDownloadDiagnostic(
            resolution.OriginalUrl,
            resolution.ActualUrl,
            download.DestinationPath,
            statusCode is null ? null : (int)statusCode.Value,
            download.LibraryName,
            download.ArtifactPath,
            resolution.RequestedSourcePreference.ToString(),
            resolution.ResolvedSourceKind,
            resolution.ResourceCategory);
        var statusText = statusCode is null
            ? "without an HTTP response"
            : $"with HTTP {(int)statusCode.Value} ({statusCode.Value})";
        var message = $"Failed to download launch file {statusText}.";
        if (innerException is null)
            return new InstanceRepairException(message, diagnostic);

        return new InstanceRepairException(message, innerException, diagnostic);
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

    private static string GetCurrentOsName()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "osx";
        return "linux";
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

    private sealed record DownloadRequest(
        string OriginalUrl,
        string DestinationPath,
        string ResourceCategory,
        string? LibraryName,
        string? ArtifactPath,
        string? ExpectedSha1 = null,
        long? ExpectedSize = null);

    private sealed class RepairDownloadSpeedReporter
    {
        private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(0.75);

        private readonly object syncRoot = new();
        private readonly IProgress<LauncherProgress>? progress;
        private long windowBytes;
        private bool hasReportedDownloadStarted;
        private DateTimeOffset windowStartedAt = DateTimeOffset.UtcNow;

        public RepairDownloadSpeedReporter(IProgress<LauncherProgress>? progress)
        {
            this.progress = progress;
        }

        public void ReportDownloadStarted()
        {
            if (progress is null)
                return;

            lock (syncRoot)
            {
                if (hasReportedDownloadStarted)
                    return;

                hasReportedDownloadStarted = true;
                windowBytes = 0;
                windowStartedAt = DateTimeOffset.UtcNow;
                progress.Report(new LauncherProgress(
                    LaunchProgressStages.DownloadSpeed,
                    string.Empty,
                    DownloadSpeedText: "0 B/s"));
            }
        }

        public void ReportDownloadedBytes(long bytesDelta)
        {
            if (progress is null || bytesDelta <= 0)
                return;

            lock (syncRoot)
            {
                windowBytes += bytesDelta;
                var now = DateTimeOffset.UtcNow;
                var elapsed = now - windowStartedAt;
                if (elapsed < SampleWindow)
                    return;

                var speedText = FormatSpeed(windowBytes / elapsed.TotalSeconds);
                windowBytes = 0;
                windowStartedAt = now;
                progress.Report(new LauncherProgress(
                    LaunchProgressStages.DownloadSpeed,
                    string.Empty,
                    DownloadSpeedText: speedText));
            }
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024 * 1024)
                return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";

            if (bytesPerSecond >= 1024)
                return $"{bytesPerSecond / 1024:0.0} KB/s";

            return $"{bytesPerSecond:0} B/s";
        }
    }

    private sealed record LibraryArtifact(
        string Url,
        string RelativePath,
        string? LibraryName,
        string ResourceCategory,
        string? Sha1 = null,
        long? Size = null);
}
