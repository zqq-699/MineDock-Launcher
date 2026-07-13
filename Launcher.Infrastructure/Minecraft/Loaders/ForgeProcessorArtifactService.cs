/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
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

internal sealed record ForgeProcessorArtifact(string RelativePath, string Sha1);

internal sealed record ForgeProcessorArtifactManifest(
    string MinecraftVersion,
    string ForgeVersion,
    IReadOnlyList<ForgeProcessorArtifact> Artifacts);

internal sealed record ForgeProcessorExpectedArtifact(string RelativePath, string? TrustedSha1);

/// <summary>
/// Reads Forge's official installer metadata and guards client processor outputs plus embedded runtime libraries.
/// </summary>
internal sealed partial class ForgeProcessorArtifactService
{
    private const int ManifestSchemaVersion = 2;
    private const string InstallerBaseUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepairLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex ForgeVersionArgumentRegex = new(
        @"(?:^|\s)--fml\.forgeVersion(?:=|\s+)(?<version>[^\s]+)",
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

    public ForgeProcessorArtifactService(
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

    public async Task<ForgeProcessorArtifactManifest> ValidateInstallerOutputsAsync(
        string installerJarPath,
        string minecraftDirectory,
        string minecraftVersion,
        string forgeVersion,
        CancellationToken cancellationToken)
    {
        var expectedArtifacts = await ReadExpectedArtifactsAsync(installerJarPath, cancellationToken).ConfigureAwait(false);
        if (expectedArtifacts.Count == 0)
            return new ForgeProcessorArtifactManifest(minecraftVersion, forgeVersion, []);

        var artifacts = new List<ForgeProcessorArtifact>(expectedArtifacts.Count);
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
                    $"Forge processor output is missing or invalid ({status}): {expected.RelativePath}");
            }

            var sha1 = await Task.Run(() => AtomicSharedFilePublisher.ComputeSha1(path), cancellationToken)
                .ConfigureAwait(false);
            artifacts.Add(new ForgeProcessorArtifact(expected.RelativePath, sha1));
        }

        return new ForgeProcessorArtifactManifest(minecraftVersion, forgeVersion, artifacts);
    }

    public async Task ValidateManifestAsync(
        string minecraftDirectory,
        ForgeProcessorArtifactManifest manifest,
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
                    $"Forge processor output is missing or invalid ({status}): {artifact.RelativePath}");
            }
        }
    }

    public static void ApplyManifest(JsonObject versionJson, ForgeProcessorArtifactManifest manifest)
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

        launcher["forgeProcessorArtifacts"] = new JsonObject
        {
            ["schemaVersion"] = ManifestSchemaVersion,
            ["minecraftVersion"] = manifest.MinecraftVersion,
            ["forgeVersion"] = manifest.ForgeVersion,
            ["artifacts"] = artifacts
        };
        versionJson["launcher"] = launcher;
    }

    public static ForgeProcessorArtifactManifest? ReadManifest(JsonObject versionJson)
    {
        if (versionJson["launcher"] is not JsonObject launcher
            || launcher["forgeProcessorArtifacts"] is not JsonObject metadata
            || metadata["schemaVersion"] is not JsonValue schemaVersionValue
            || !schemaVersionValue.TryGetValue<int>(out var schemaVersion)
            || schemaVersion != ManifestSchemaVersion
            || metadata["artifacts"] is not JsonArray artifacts)
        {
            return null;
        }

        var minecraftVersion = TryReadString(metadata["minecraftVersion"]);
        var forgeVersion = TryReadString(metadata["forgeVersion"]);
        if (string.IsNullOrWhiteSpace(minecraftVersion) || string.IsNullOrWhiteSpace(forgeVersion))
            return null;

        var parsed = new List<ForgeProcessorArtifact>();
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

            parsed.Add(new ForgeProcessorArtifact(relativePath, sha1!));
        }

        return parsed.Count == 0
            ? null
            : new ForgeProcessorArtifactManifest(minecraftVersion, forgeVersion, parsed);
    }

    private static string? TryReadString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text.Trim()
            : null;
    }

    public static bool TryResolveForgeIdentity(
        JsonObject versionJson,
        out string minecraftVersion,
        out string forgeVersion)
    {
        minecraftVersion = LauncherVersionMetadata.ReadMinecraftVersion(
            JsonSerializer.SerializeToElement(versionJson));
        forgeVersion = string.Empty;

        var argumentText = string.Join(' ', EnumerateStringValues(versionJson));
        var forgeArgument = ForgeVersionArgumentRegex.Match(argumentText);
        if (forgeArgument.Success)
            forgeVersion = forgeArgument.Groups["version"].Value.Trim();

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
                if (parts.Length < 3
                    || !parts[0].Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase)
                    || (!parts[1].Equals("forge", StringComparison.OrdinalIgnoreCase)
                        && !parts[1].Equals("fmlloader", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var separator = parts[2].IndexOf('-');
                if (string.IsNullOrWhiteSpace(minecraftVersion) && separator > 0)
                    minecraftVersion = parts[2][..separator];
                if (string.IsNullOrWhiteSpace(forgeVersion))
                    forgeVersion = separator >= 0 && separator < parts[2].Length - 1
                        ? parts[2][(separator + 1)..]
                        : parts[2];
            }
        }

        return !string.IsNullOrWhiteSpace(minecraftVersion) && !string.IsNullOrWhiteSpace(forgeVersion);
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

    private static string ResolveLibraryPath(string minecraftDirectory, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
            throw new InvalidDataException($"Unsafe Forge processor artifact path: {relativePath}");

        var librariesRoot = Path.GetFullPath(Path.Combine(minecraftDirectory, "libraries"));
        var path = Path.GetFullPath(Path.Combine(librariesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = librariesRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Forge processor artifact escaped the libraries directory: {relativePath}");
        return path;
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath)
               && !Path.IsPathRooted(relativePath)
               && !relativePath.Split('/', '\\').Any(segment => segment is "" or "." or "..");
    }
}
