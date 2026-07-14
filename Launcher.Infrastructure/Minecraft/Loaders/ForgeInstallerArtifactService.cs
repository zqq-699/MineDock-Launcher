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
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record ForgeInstallerLibrary(ManagedLibraryArtifact Artifact, string? EmbeddedEntryName);

internal sealed record ForgeProcessorOutput(string RelativePath, string? TrustedSha1);

internal sealed record ForgeInstallerPlan(
    IReadOnlyList<ForgeInstallerLibrary> PrerequisiteLibraries,
    IReadOnlyList<ForgeInstallerLibrary> RuntimeLibraries,
    IReadOnlyList<ForgeProcessorOutput> ProcessorOutputs,
    JsonArray RuntimeLibraryMetadata)
{
    public IEnumerable<ForgeInstallerLibrary> AllLibraries => PrerequisiteLibraries.Concat(RuntimeLibraries);
}

/// <summary>
/// Materializes the libraries declared by a Forge installer and normalizes the final
/// version JSON. All requirements come from installer metadata; the installer archive
/// only chooses between an embedded copy and the normal Maven download path.
/// </summary>
internal sealed partial class LoaderInstallerArtifactService
{
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

    public LoaderInstallerArtifactService(
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

    public static bool HasLegacyManifest(JsonObject versionJson, string metadataKey) =>
        versionJson["launcher"] is JsonObject launcher && launcher.ContainsKey(metadataKey);

    public async Task<ForgeInstallerPlan> ReadPlanAsync(string installerJarPath, CancellationToken cancellationToken)
    {
        await using var installerStream = new FileStream(
            installerJarPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(installerStream, ZipArchiveMode.Read, leaveOpen: false);
        var profile = await ReadObjectAsync(archive, "install_profile.json", required: true, cancellationToken).ConfigureAwait(false);
        var installerVersion = await ReadObjectAsync(archive, "version.json", required: false, cancellationToken).ConfigureAwait(false);

        var embeddedEntries = archive.Entries
            .Where(entry => entry.Length > 0 && entry.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                entry => entry.FullName["maven/".Length..].Replace('\\', '/'),
                entry => entry.FullName,
                StringComparer.OrdinalIgnoreCase);

        var prerequisiteLibraries = new Dictionary<string, ForgeInstallerLibrary>(StringComparer.OrdinalIgnoreCase);
        AddLibraries(profile["libraries"] as JsonArray, embeddedEntries, prerequisiteLibraries);
        var processorOutputs = ReadProcessorRequirements(profile, embeddedEntries, prerequisiteLibraries);

        var runtimeLibraries = new Dictionary<string, ForgeInstallerLibrary>(StringComparer.OrdinalIgnoreCase);
        var runtimeMetadata = installerVersion["libraries"] as JsonArray ?? new JsonArray();
        AddLibraries(runtimeMetadata, embeddedEntries, runtimeLibraries);

        return new ForgeInstallerPlan(
            prerequisiteLibraries.Values.OrderBy(item => item.Artifact.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            runtimeLibraries.Values.OrderBy(item => item.Artifact.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            processorOutputs.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            (JsonArray)runtimeMetadata.DeepClone());
    }

    public async Task MaterializePrerequisitesAsync(
        string installerJarPath,
        ForgeInstallerPlan plan,
        string minecraftDirectory,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        await MaterializeLibrariesAsync(installerJarPath, plan.PrerequisiteLibraries, minecraftDirectory,
            downloadSourcePreference, downloadSpeedLimitMbPerSecond, cancellationToken).ConfigureAwait(false);
    }

    public async Task MaterializeRuntimeLibrariesAsync(
        string installerJarPath,
        ForgeInstallerPlan plan,
        string minecraftDirectory,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        await MaterializeLibrariesAsync(installerJarPath, plan.RuntimeLibraries, minecraftDirectory,
            downloadSourcePreference, downloadSpeedLimitMbPerSecond, cancellationToken).ConfigureAwait(false);
    }

    public async Task ValidatePublishedArtifactsAsync(
        string minecraftDirectory,
        ForgeInstallerPlan plan,
        CancellationToken cancellationToken)
    {
        foreach (var library in plan.AllLibraries)
        {
            var path = ResolveLibraryPath(minecraftDirectory, library.Artifact.RelativePath);
            await EnsureValidAsync(path, library.Artifact.Sha1, library.Artifact.Size, "Forge library", cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var output in plan.ProcessorOutputs)
        {
            var path = ResolveLibraryPath(minecraftDirectory, output.RelativePath);
            await EnsureValidAsync(path, output.TrustedSha1, expectedSize: null, "Forge processor output", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static async Task ApplyRuntimeLibrariesAsync(
        string versionJsonPath,
        ForgeInstallerPlan plan,
        string legacyMetadataKey,
        CancellationToken cancellationToken)
    {
        var versionJson = await ReadVersionJsonAsync(versionJsonPath, cancellationToken).ConfigureAwait(false);
        ApplyRuntimeLibraries(versionJson, plan, legacyMetadataKey);
        await AtomicJsonFileWriter.WriteAsync(versionJsonPath, versionJson, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public static void ApplyRuntimeLibraries(JsonObject versionJson, ForgeInstallerPlan plan, string legacyMetadataKey)
    {
        versionJson["libraries"] = VersionJsonMergeHelper.MergeLibraries(
            plan.RuntimeLibraryMetadata,
            versionJson["libraries"] as JsonArray);
        RemoveLegacyManifest(versionJson, legacyMetadataKey);
    }

    public async Task<JsonObject> RepairLegacyVersionAsync(
        string minecraftDirectory,
        string versionJsonPath,
        JsonObject versionJson,
        string minecraftVersion,
        string loaderVersion,
        string legacyMetadataKey,
        string loaderName,
        string installerUrl,
        IProgress<LauncherProgress>? progress,
        bool allowRepair,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        if (!HasLegacyManifest(versionJson, legacyMetadataKey))
            return versionJson;
        if (!allowRepair)
        {
            throw new InstanceRepairException(
                $"{loaderName} runtime metadata requires repair and automatic repair is disabled.");
        }

        var repairKey = $"{Path.GetFullPath(minecraftDirectory)}|{loaderName}|{minecraftVersion}|{loaderVersion}";
        var repairLock = RepairLocks.GetOrAdd(repairKey, static _ => new SemaphoreSlim(1, 1));
        await repairLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentVersionJson = await ReadVersionJsonAsync(versionJsonPath, cancellationToken).ConfigureAwait(false);
            if (!HasLegacyManifest(currentVersionJson, legacyMetadataKey))
                return currentVersionJson;

            var sessionDirectory = Path.Combine(tempRootDirectory, $"launcher-{loaderName.ToLowerInvariant()}-repair", Guid.NewGuid().ToString("N"));
            var installerJarPath = Path.Combine(sessionDirectory, $"{loaderName.ToLowerInvariant()}-{loaderVersion}-installer.jar");
            var installerMinecraftDirectory = Path.Combine(sessionDirectory, ".minecraft");
            Directory.CreateDirectory(sessionDirectory);
            try
            {
                progress?.Report(new LauncherProgress(LaunchProgressStages.RepairingLoaderInstaller, string.Empty, 21));
                using var speedReporter = new SlidingWindowDownloadSpeedReporter(
                    progress,
                    speedStage: LaunchProgressStages.RepairingLoaderInstaller,
                    inactiveStage: LaunchProgressStages.RepairingLoaderInstaller);
                await DownloadInstallerAsync(installerJarPath, installerUrl, loaderName, downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond, cancellationToken, speedReporter).ConfigureAwait(false);
                var plan = await ReadPlanAsync(installerJarPath, cancellationToken).ConfigureAwait(false);

                progress?.Report(new LauncherProgress(LaunchProgressStages.RepairingLibraries, string.Empty, 24));
                LoaderVersionDirectoryTransaction.EnsureLauncherProfileExists(installerMinecraftDirectory);
                Directory.CreateDirectory(Path.Combine(installerMinecraftDirectory, "versions"));
                var prerequisiteSeeder = new LoaderInstallerPrerequisiteSeeder(logger);
                var snapshot = await prerequisiteSeeder.SeedAsync(minecraftDirectory, installerMinecraftDirectory, minecraftVersion,
                    installerJarPath, cancellationToken).ConfigureAwait(false);
                await MaterializePrerequisitesAsync(installerJarPath, plan, installerMinecraftDirectory, downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond, cancellationToken).ConfigureAwait(false);
                await EnsureVanillaVersionAsync(installerMinecraftDirectory, minecraftVersion, downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond, cancellationToken).ConfigureAwait(false);
                progress?.Report(new LauncherProgress(LaunchProgressStages.RunningLoaderInstaller, string.Empty, 27));
                await installerRunner.RunInstallerAsync("java", installerJarPath, installerMinecraftDirectory, cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(new LauncherProgress(LaunchProgressStages.RepairingLibraries, string.Empty, 29));
                await MaterializeRuntimeLibrariesAsync(installerJarPath, plan, installerMinecraftDirectory, downloadSourcePreference,
                    downloadSpeedLimitMbPerSecond, cancellationToken).ConfigureAwait(false);
                await ValidatePublishedArtifactsAsync(installerMinecraftDirectory, plan, cancellationToken).ConfigureAwait(false);

                await prerequisiteSeeder.PublishDeltaAsync(snapshot, minecraftDirectory, cancellationToken).ConfigureAwait(false);
                await ValidatePublishedArtifactsAsync(minecraftDirectory, plan, cancellationToken).ConfigureAwait(false);

                ApplyRuntimeLibraries(currentVersionJson, plan, legacyMetadataKey);
                await AtomicJsonFileWriter.WriteAsync(versionJsonPath, currentVersionJson, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "Migrated legacy loader runtime metadata. Loader={Loader} MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} LibraryCount={LibraryCount} ProcessorOutputCount={ProcessorOutputCount}",
                    loaderName, minecraftVersion, loaderVersion, plan.RuntimeLibraries.Count, plan.ProcessorOutputs.Count);
                return currentVersionJson;
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
                throw new InstanceRepairException($"{loaderName} {loaderVersion} runtime metadata could not be repaired.", exception);
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

    public static bool TryResolveForgeIdentity(JsonObject versionJson, out string minecraftVersion, out string forgeVersion)
    {
        minecraftVersion = LauncherVersionMetadata.ReadMinecraftVersion(JsonSerializer.SerializeToElement(versionJson));
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
                var parts = name?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts is null || parts.Length < 3 || !parts[0].Equals("net.minecraftforge", StringComparison.OrdinalIgnoreCase)
                    || (!parts[1].Equals("forge", StringComparison.OrdinalIgnoreCase) && !parts[1].Equals("fmlloader", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var separator = parts[2].IndexOf('-');
                if (string.IsNullOrWhiteSpace(minecraftVersion) && separator > 0)
                    minecraftVersion = parts[2][..separator];
                if (string.IsNullOrWhiteSpace(forgeVersion))
                    forgeVersion = separator >= 0 && separator < parts[2].Length - 1 ? parts[2][(separator + 1)..] : parts[2];
            }
        }
        return !string.IsNullOrWhiteSpace(minecraftVersion) && !string.IsNullOrWhiteSpace(forgeVersion);
    }

    public static bool TryResolveNeoForgeIdentity(JsonObject versionJson, out string minecraftVersion, out string neoForgeVersion)
    {
        minecraftVersion = LauncherVersionMetadata.ReadMinecraftVersion(JsonSerializer.SerializeToElement(versionJson));
        neoForgeVersion = string.Empty;
        var argumentText = string.Join(' ', EnumerateStringValues(versionJson));
        var neoForgeArgument = Regex.Match(argumentText, @"(?:^|\s)--fml\.neoForgeVersion(?:=|\s+)(?<version>[^\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (neoForgeArgument.Success)
            neoForgeVersion = neoForgeArgument.Groups["version"].Value.Trim();
        var minecraftArgument = MinecraftVersionArgumentRegex.Match(argumentText);
        if (string.IsNullOrWhiteSpace(minecraftVersion) && minecraftArgument.Success)
            minecraftVersion = minecraftArgument.Groups["version"].Value.Trim();
        if (versionJson["libraries"] is JsonArray libraries)
        {
            foreach (var library in libraries.OfType<JsonObject>())
            {
                var parts = library["name"]?.GetValue<string>()?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts is { Length: >= 3 }
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

    private async Task MaterializeLibrariesAsync(
        string installerJarPath,
        IEnumerable<ForgeInstallerLibrary> libraries,
        string minecraftDirectory,
        DownloadSourcePreference downloadSourcePreference,
        int downloadSpeedLimitMbPerSecond,
        CancellationToken cancellationToken)
    {
        var archiveEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        await using (var stream = new FileStream(installerJarPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
        {
            foreach (var library in libraries.Where(item => item.EmbeddedEntryName is not null))
            {
                var entry = archive.GetEntry(library.EmbeddedEntryName!);
                if (entry is null || entry.Length == 0)
                    throw new InvalidDataException($"Forge installer embedded library is missing: {library.Artifact.RelativePath}");
                archiveEntries[library.Artifact.RelativePath] = entry;
            }

            foreach (var library in libraries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = ResolveLibraryPath(minecraftDirectory, library.Artifact.RelativePath);
                var status = await MinecraftFileIntegrity.EvaluateAsync(destinationPath, library.Artifact.Sha1, library.Artifact.Size,
                    MinecraftFileVerification.Full, cancellationToken).ConfigureAwait(false);
                if (status == MinecraftFileIntegrityStatus.Valid)
                    continue;

                if (archiveEntries.TryGetValue(library.Artifact.RelativePath, out var entry))
                {
                    await CopyEmbeddedLibraryAsync(entry, destinationPath, library.Artifact, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var executor = new MinecraftDownloadRequestExecutor(httpClient, logger,
                    DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
                    category: DownloadConcurrencyCategory.Runtime);
                await executor.DownloadFileAsync(library.Artifact.Url, downloadSourcePreference, library.Artifact.ResourceCategory,
                    destinationPath, library.Artifact.Sha1, library.Artifact.Size, reportDownloadedBytes: null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task CopyEmbeddedLibraryAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        ManagedLibraryArtifact artifact,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.download";
        try
        {
            await using (var source = entry.Open())
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            await EnsureValidAsync(temporaryPath, artifact.Sha1, artifact.Size, "Embedded Forge library", cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task EnsureValidAsync(
        string path, string? expectedSha1, long? expectedSize, string kind, CancellationToken cancellationToken)
    {
        var status = await MinecraftFileIntegrity.EvaluateAsync(path, expectedSha1, expectedSize, MinecraftFileVerification.Full, cancellationToken)
            .ConfigureAwait(false);
        if (status != MinecraftFileIntegrityStatus.Valid)
            throw new InvalidDataException($"{kind} is missing or invalid ({status}): {path}");
    }

    private static async Task<JsonObject> ReadObjectAsync(ZipArchive archive, string entryName, bool required, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            if (required)
                throw new InvalidDataException($"Forge installer does not contain {entryName}.");
            return new JsonObject();
        }
        await using var stream = entry.Open();
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException($"Forge installer {entryName} is empty or invalid.");
    }

    private static void AddLibraries(
        JsonArray? libraries,
        IReadOnlyDictionary<string, string> embeddedEntries,
        IDictionary<string, ForgeInstallerLibrary> destination)
    {
        if (libraries is null)
            return;
        foreach (var library in libraries.OfType<JsonObject>())
        {
            if (!ManagedLibraryArtifactResolver.IsAllowed(library))
                continue;
            var resolved = ManagedLibraryArtifactResolver.EnumerateDownloads(library).ToList();
            if (resolved.Count == 0)
                throw new InvalidDataException($"Forge installer library cannot be resolved: {library["name"]?.GetValue<string>() ?? library.ToJsonString()}");
            foreach (var artifact in resolved)
                AddLibrary(artifact, embeddedEntries, destination);
        }
    }

    private static IReadOnlyList<ForgeProcessorOutput> ReadProcessorRequirements(
        JsonObject profile,
        IReadOnlyDictionary<string, string> embeddedEntries,
        IDictionary<string, ForgeInstallerLibrary> prerequisites)
    {
        if (profile["processors"] is not JsonArray processors)
            return [];
        var data = ReadClientData(profile["data"] as JsonObject);
        var outputs = new Dictionary<string, ForgeProcessorOutput>(StringComparer.OrdinalIgnoreCase);
        foreach (var processor in processors.OfType<JsonObject>())
        {
            if (!RunsOnClient(processor))
                continue;
            AddCoordinate(processor["jar"]?.GetValue<string>(), embeddedEntries, prerequisites);
            if (processor["classpath"] is JsonArray classpath)
            {
                foreach (var item in classpath)
                    AddCoordinate(item?.GetValue<string>(), embeddedEntries, prerequisites);
            }
            if (processor["outputs"] is JsonObject declaredOutputs)
            {
                foreach (var output in declaredOutputs)
                    AddOutput(outputs, output.Key, output.Value?.GetValue<string>(), data);
            }
            if (processor["args"] is not JsonArray arguments)
                continue;
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                var argument = arguments[index]?.GetValue<string>();
                if (argument is "--output" or "--slim" or "--extra")
                    AddOutput(outputs, arguments[index + 1]?.GetValue<string>(), hashExpression: null, data);
            }
        }
        return outputs.Values.ToArray();
    }

    private static void AddCoordinate(string? coordinate, IReadOnlyDictionary<string, string> embeddedEntries,
        IDictionary<string, ForgeInstallerLibrary> prerequisites)
    {
        if (string.IsNullOrWhiteSpace(coordinate))
            return;
        AddLibraries(new JsonArray(new JsonObject { ["name"] = coordinate.Trim() }), embeddedEntries, prerequisites);
    }

    private static void AddLibrary(ManagedLibraryArtifact artifact, IReadOnlyDictionary<string, string> embeddedEntries,
        IDictionary<string, ForgeInstallerLibrary> destination)
    {
        if (!IsSafeRelativePath(artifact.RelativePath))
            throw new InvalidDataException($"Forge installer declared an unsafe library path: {artifact.RelativePath}");
        var normalizedPath = artifact.RelativePath.Replace('\\', '/');
        embeddedEntries.TryGetValue(normalizedPath, out var embeddedEntryName);
        var candidate = new ForgeInstallerLibrary(artifact with { RelativePath = normalizedPath }, embeddedEntryName);
        if (!destination.TryGetValue(normalizedPath, out var existing))
        {
            destination[normalizedPath] = candidate;
            return;
        }
        if (!Compatible(existing.Artifact.Sha1, candidate.Artifact.Sha1)
            || !Compatible(existing.Artifact.Size, candidate.Artifact.Size))
        {
            throw new InvalidDataException($"Forge installer declared conflicting library metadata: {normalizedPath}");
        }
        var selected = new ManagedLibraryArtifact(
            existing.Artifact.Url,
            normalizedPath,
            existing.Artifact.LibraryName ?? candidate.Artifact.LibraryName,
            existing.Artifact.ResourceCategory,
            existing.Artifact.Sha1 ?? candidate.Artifact.Sha1,
            existing.Artifact.Size ?? candidate.Artifact.Size);
        destination[normalizedPath] = new ForgeInstallerLibrary(selected, existing.EmbeddedEntryName ?? candidate.EmbeddedEntryName);
    }

    private static bool Compatible(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Compatible(long? left, long? right) => left is null || right is null || left == right;

    private static Dictionary<string, string> ReadClientData(JsonObject? data)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (data is null)
            return values;
        foreach (var pair in data)
        {
            if (pair.Value is JsonObject sides && sides["client"] is JsonValue client && client.TryGetValue<string>(out var value)
                && !string.IsNullOrWhiteSpace(value))
                values[pair.Key] = value;
        }
        return values;
    }

    private static void AddOutput(IDictionary<string, ForgeProcessorOutput> outputs, string? pathExpression,
        string? hashExpression, IReadOnlyDictionary<string, string> data)
    {
        if (string.IsNullOrWhiteSpace(pathExpression))
            return;
        var token = TryReadSinglePlaceholder(pathExpression);
        var coordinateExpression = ResolveExpression(pathExpression, data).Trim();
        if (coordinateExpression.Length < 3 || coordinateExpression[0] != '[' || coordinateExpression[^1] != ']'
            || !ManagedLibraryArtifactResolver.TryBuildMavenPath(coordinateExpression[1..^1], classifierOverride: null, out var relativePath)
            || !IsSafeRelativePath(relativePath))
            throw new InvalidDataException($"Forge processor output cannot be resolved: {pathExpression}");
        var resolvedHash = TrimLiteral(ResolveExpression(hashExpression, data));
        if (!MinecraftFileIntegrity.IsSha1(resolvedHash) && token is not null && data.TryGetValue($"{token}_SHA", out var tokenHash))
            resolvedHash = TrimLiteral(tokenHash);
        if (!MinecraftFileIntegrity.IsSha1(resolvedHash))
            resolvedHash = null;
        if (outputs.TryGetValue(relativePath, out var existing) && !Compatible(existing.TrustedSha1, resolvedHash))
            throw new InvalidDataException($"Forge processor declared conflicting output checksums: {relativePath}");
        outputs[relativePath] = new ForgeProcessorOutput(relativePath, existing?.TrustedSha1 ?? resolvedHash);
    }

    private async Task DownloadInstallerAsync(string installerJarPath, string installerUrl, string loaderName,
        DownloadSourcePreference preference, int speedLimit, CancellationToken cancellationToken,
        SlidingWindowDownloadSpeedReporter? speedReporter = null)
    {
        var executor = new MinecraftDownloadRequestExecutor(httpClient, logger,
            DownloadBandwidthLimiter.Create(speedLimit, downloadSpeedLimitState), category: DownloadConcurrencyCategory.Runtime);
        using var speedSession = speedReporter is null ? null : new DownloadActivitySpeedSession(speedReporter);
        await executor.DownloadFileAsync(installerUrl, preference, loaderName, installerJarPath,
            expectedSha1: null,
            expectedSize: null,
            reportDownloadedBytes: speedReporter is null ? null : bytes => speedReporter.ReportNetworkBytes(bytes),
            cancellationToken,
            reportActivity: speedSession is null ? null : activity => speedSession.Report(activity)).ConfigureAwait(false);
    }

    private async Task EnsureVanillaVersionAsync(string installerMinecraftDirectory, string minecraftVersion,
        DownloadSourcePreference preference, int speedLimit, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(installerMinecraftDirectory, "versions", minecraftVersion);
        if (File.Exists(Path.Combine(directory, $"{minecraftVersion}.json")) && File.Exists(Path.Combine(directory, $"{minecraftVersion}.jar")))
            return;
        await finalVersionInstaller.InstallAsync(installerMinecraftDirectory, minecraftVersion, preference, progress: null,
            cancellationToken, speedLimit).ConfigureAwait(false);
    }

    private static void RemoveLegacyManifest(JsonObject versionJson, string metadataKey)
    {
        if (versionJson["launcher"] is not JsonObject launcher)
            return;
        launcher.Remove(metadataKey);
        if (launcher.Count == 0)
            versionJson.Remove("launcher");
    }

    private static async Task<JsonObject> ReadVersionJsonAsync(string versionJsonPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(versionJsonPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject
            ?? throw new InstanceRepairException($"Version metadata is empty: {versionJsonPath}");
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

    private static bool RunsOnClient(JsonObject processor) =>
        processor["sides"] is not JsonArray sides || sides.Count == 0
            || sides.Any(side => string.Equals(side?.GetValue<string>(), "client", StringComparison.OrdinalIgnoreCase));

    private static readonly Regex PlaceholderRegex = new("\\{(?<name>[A-Z0-9_]+)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string ResolveExpression(string? expression, IReadOnlyDictionary<string, string> values) =>
        string.IsNullOrWhiteSpace(expression) ? string.Empty : PlaceholderRegex.Replace(expression,
            match => values.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);

    private static string? TryReadSinglePlaceholder(string expression)
    {
        var trimmed = expression.Trim();
        var match = PlaceholderRegex.Match(trimmed);
        return match.Success && match.Index == 0 && match.Length == trimmed.Length ? match.Groups["name"].Value : null;
    }

    private static string? TrimLiteral(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('\'', '"');

    private static string ResolveLibraryPath(string minecraftDirectory, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
            throw new InvalidDataException($"Unsafe Forge library path: {relativePath}");
        var root = Path.Combine(minecraftDirectory, "libraries");
        return MinecraftPathGuard.EnsureWithin(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)), root, "Forge library");
    }

    private static bool IsSafeRelativePath(string relativePath) => !string.IsNullOrWhiteSpace(relativePath)
        && !Path.IsPathRooted(relativePath) && !relativePath.Split('/', '\\').Any(segment => segment is "" or "." or "..");

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
