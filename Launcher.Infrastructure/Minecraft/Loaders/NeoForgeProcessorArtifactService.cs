/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record NeoForgeProcessorArtifact(string RelativePath, string Sha1);

internal sealed record NeoForgeProcessorArtifactManifest(
    string MinecraftVersion,
    string NeoForgeVersion,
    IReadOnlyList<NeoForgeProcessorArtifact> Artifacts);

internal sealed record NeoForgeProcessorExpectedArtifact(string RelativePath, string? TrustedSha1);

/// <summary>
/// Guards NeoForge client processor outputs and installer-embedded runtime libraries.
/// </summary>
internal sealed partial class NeoForgeProcessorArtifactService
{
    private const int ManifestSchemaVersion = 1;
    private const string InstallerBaseUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepairLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex NeoForgeVersionArgumentRegex = new(
        @"(?:^|\s)--fml\.neoForgeVersion(?:=|\s+)(?<version>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MinecraftVersionArgumentRegex = new(
        @"(?:^|\s)--fml\.mcVersion(?:=|\s+)(?<version>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly HttpClient httpClient;
    private readonly IForgeInstallerRunner installerRunner;
    private readonly IFinalVersionInstaller finalVersionInstaller;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;
    private readonly string tempRootDirectory;

    public NeoForgeProcessorArtifactService(
        HttpClient httpClient,
        IForgeInstallerRunner? installerRunner = null,
        IFinalVersionInstaller? finalVersionInstaller = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null,
        string? tempRootDirectory = null)
    {
        this.httpClient = httpClient;
        this.installerRunner = installerRunner ?? new ForgeInstallerRunner();
        this.finalVersionInstaller = finalVersionInstaller ?? new FinalVersionInstaller();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger.Instance;
        this.tempRootDirectory = string.IsNullOrWhiteSpace(tempRootDirectory) ? Path.GetTempPath() : tempRootDirectory;
    }

    public async Task<NeoForgeProcessorArtifactManifest> ValidateInstallerOutputsAsync(
        string installerJarPath,
        string minecraftDirectory,
        string minecraftVersion,
        string neoForgeVersion,
        CancellationToken cancellationToken)
    {
        var expectedArtifacts = await ReadExpectedArtifactsAsync(installerJarPath, cancellationToken).ConfigureAwait(false);
        if (expectedArtifacts.Count == 0)
        {
            throw new InvalidDataException(
                $"NeoForge {neoForgeVersion} installer did not declare any client processor artifacts.");
        }

        var artifacts = new List<NeoForgeProcessorArtifact>(expectedArtifacts.Count);
        foreach (var expected in expectedArtifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolveLibraryPath(minecraftDirectory, expected.RelativePath);
            var status = await MinecraftFileIntegrity.EvaluateAsync(
                path,
                expected.TrustedSha1,
                expectedSize: null,
                MinecraftFileVerification.Full,
                cancellationToken).ConfigureAwait(false);
            if (status is not MinecraftFileIntegrityStatus.Valid)
            {
                throw new InvalidDataException(
                    $"NeoForge processor output is missing or invalid ({status}): {expected.RelativePath}");
            }

            var sha1 = await Task.Run(() => AtomicSharedFilePublisher.ComputeSha1(path), cancellationToken)
                .ConfigureAwait(false);
            artifacts.Add(new NeoForgeProcessorArtifact(expected.RelativePath, sha1));
        }

        return new NeoForgeProcessorArtifactManifest(minecraftVersion, neoForgeVersion, artifacts);
    }

    public async Task ValidateManifestAsync(
        string minecraftDirectory,
        NeoForgeProcessorArtifactManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var artifact in manifest.Artifacts)
        {
            var path = ResolveLibraryPath(minecraftDirectory, artifact.RelativePath);
            var status = await MinecraftFileIntegrity.EvaluateAsync(
                path,
                artifact.Sha1,
                expectedSize: null,
                MinecraftFileVerification.Full,
                cancellationToken).ConfigureAwait(false);
            if (status is not MinecraftFileIntegrityStatus.Valid)
            {
                throw new InstanceRepairException(
                    $"NeoForge processor output is missing or invalid ({status}): {artifact.RelativePath}");
            }
        }
    }

    public static void ApplyManifest(JsonObject versionJson, NeoForgeProcessorArtifactManifest manifest)
    {
        var launcher = versionJson["launcher"] as JsonObject ?? new JsonObject();
        var artifacts = new JsonArray();
        foreach (var artifact in manifest.Artifacts)
        {
            artifacts.Add(new JsonObject
            {
                ["path"] = artifact.RelativePath,
                ["sha1"] = artifact.Sha1.ToLowerInvariant()
            });
        }

        launcher["neoForgeProcessorArtifacts"] = new JsonObject
        {
            ["schemaVersion"] = ManifestSchemaVersion,
            ["minecraftVersion"] = manifest.MinecraftVersion,
            ["neoForgeVersion"] = manifest.NeoForgeVersion,
            ["artifacts"] = artifacts
        };
        versionJson["launcher"] = launcher;
    }

    public static NeoForgeProcessorArtifactManifest? ReadManifest(JsonObject versionJson)
    {
        if (versionJson["launcher"] is not JsonObject launcher
            || launcher["neoForgeProcessorArtifacts"] is not JsonObject metadata
            || metadata["schemaVersion"] is not JsonValue schemaVersionValue
            || !schemaVersionValue.TryGetValue<int>(out var schemaVersion)
            || schemaVersion != ManifestSchemaVersion
            || metadata["artifacts"] is not JsonArray artifacts)
        {
            return null;
        }

        var minecraftVersion = TryReadString(metadata["minecraftVersion"]);
        var neoForgeVersion = TryReadString(metadata["neoForgeVersion"]);
        if (string.IsNullOrWhiteSpace(minecraftVersion) || string.IsNullOrWhiteSpace(neoForgeVersion))
            return null;

        var parsed = new List<NeoForgeProcessorArtifact>();
        foreach (var item in artifacts.OfType<JsonObject>())
        {
            var relativePath = TryReadString(item["path"])?.Replace('\\', '/');
            var sha1 = TryReadString(item["sha1"]);
            if (string.IsNullOrWhiteSpace(relativePath)
                || !IsSafeRelativePath(relativePath)
                || !MinecraftFileIntegrity.IsSha1(sha1))
            {
                return null;
            }

            parsed.Add(new NeoForgeProcessorArtifact(relativePath, sha1!));
        }

        return parsed.Count == 0
            ? null
            : new NeoForgeProcessorArtifactManifest(minecraftVersion, neoForgeVersion, parsed);
    }

    public static bool TryResolveNeoForgeIdentity(
        JsonObject versionJson,
        out string minecraftVersion,
        out string neoForgeVersion)
    {
        minecraftVersion = LauncherVersionMetadata.ReadMinecraftVersion(
            JsonSerializer.SerializeToElement(versionJson));
        neoForgeVersion = string.Empty;

        var argumentText = string.Join(' ', EnumerateStringValues(versionJson));
        var neoForgeArgument = NeoForgeVersionArgumentRegex.Match(argumentText);
        if (neoForgeArgument.Success)
            neoForgeVersion = neoForgeArgument.Groups["version"].Value.Trim();

        var minecraftArgument = MinecraftVersionArgumentRegex.Match(argumentText);
        if (string.IsNullOrWhiteSpace(minecraftVersion) && minecraftArgument.Success)
            minecraftVersion = minecraftArgument.Groups["version"].Value.Trim();

        if (versionJson["libraries"] is JsonArray libraries)
        {
            foreach (var library in libraries.OfType<JsonObject>())
            {
                var name = library["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var parts = name.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3
                    && parts[0].Equals("net.neoforged", StringComparison.OrdinalIgnoreCase)
                    && parts[1].Equals("neoforge", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(neoForgeVersion))
                {
                    neoForgeVersion = parts[2];
                }
            }
        }

        return !string.IsNullOrWhiteSpace(minecraftVersion) && !string.IsNullOrWhiteSpace(neoForgeVersion);
    }

    private static IEnumerable<string> EnumerateStringValues(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            yield return text;
            yield break;
        }

        if (node is JsonObject obj)
        {
            foreach (var child in obj.Select(pair => pair.Value).Where(child => child is not null))
            foreach (var childText in EnumerateStringValues(child!))
                yield return childText;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(child => child is not null))
            foreach (var childText in EnumerateStringValues(child!))
                yield return childText;
        }
    }

    private static string? TryReadString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text.Trim()
            : null;
    }

    private static string ResolveLibraryPath(string minecraftDirectory, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
            throw new InvalidDataException($"Unsafe NeoForge processor artifact path: {relativePath}");

        var librariesRoot = Path.GetFullPath(Path.Combine(minecraftDirectory, "libraries"));
        var path = Path.GetFullPath(Path.Combine(librariesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = librariesRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"NeoForge processor artifact escaped the libraries directory: {relativePath}");
        return path;
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath)
               && !Path.IsPathRooted(relativePath)
               && !relativePath.Split('/', '\\').Any(segment => segment is "" or "." or "..");
    }
}
