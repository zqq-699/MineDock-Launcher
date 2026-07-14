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
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// The single entry point for game-file validation and recovery. The existing
/// downloader and installer recovery code remains the executor; this service
/// owns the resolved manifest and the mandatory post-repair validation gate.
/// </summary>
internal sealed class GameFileIntegrityService : IGameFileIntegrityService
{
    private readonly ManagedVersionRepairService repairService;
    private readonly RequiredGameFileManifestBuilder manifestBuilder;
    private readonly ILogger logger;

    public GameFileIntegrityService(
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<GameFileIntegrityService>? logger = null)
        : this(httpClient: null, downloadSpeedLimitState, logger)
    {
    }

    internal GameFileIntegrityService(
        HttpClient? httpClient,
        IDownloadSpeedLimitState? downloadSpeedLimitState,
        ILogger? logger = null)
    {
        this.logger = logger ?? NullLogger.Instance;
        repairService = new ManagedVersionRepairService(httpClient, downloadSpeedLimitState, this.logger);
        manifestBuilder = new RequiredGameFileManifestBuilder(this.logger);
    }

    internal GameFileIntegrityService(
        ManagedVersionRepairService repairService,
        RequiredGameFileManifestBuilder manifestBuilder,
        ILogger? logger = null)
    {
        this.repairService = repairService;
        this.manifestBuilder = manifestBuilder;
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task<GameFileRepairResult> ValidateAndRepairAsync(
        GameFileIntegrityRequest request,
        GameFileRepairOptions options,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var before = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            LogReport("Game file preflight completed.", request, before, repairedCount: 0);
            if (before.LaunchAllowed || !options.AllowRepair)
                return before;

            await repairService.RepairAsync(
                    request.MinecraftDirectory,
                    request.VersionName,
                    request.InstanceDirectory,
                    progress,
                    allowRepair: true,
                    cancellationToken,
                    request.DownloadSourcePreference,
                    request.DownloadSpeedLimitMbPerSecond)
                .ConfigureAwait(false);

            var after = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            var repairedCount = Math.Max(0, before.FailedCount - after.FailedCount);
            LogReport("Game file post-repair validation completed.", request, after, repairedCount);
            return after with { RepairedCount = repairedCount };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FailureResult(request, GameFileRepairFailureReason.Canceled, "Canceled", "None", null);
        }
        catch (InstanceRepairException exception)
        {
            return FailureResult(
                request,
                ClassifyFailure(exception),
                "Repair",
                "PlannedRecovery",
                exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            logger.LogWarning(exception, "Game file integrity planning failed. VersionName={VersionName}", request.VersionName);
            return FailureResult(
                request,
                GameFileRepairFailureReason.MetadataIncomplete,
                "Metadata",
                "None",
                exception.Message);
        }
    }

    public async Task<GameFileRepairResult> ValidateFinalLaunchCommandAsync(
        GameFileIntegrityRequest request,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        var plan = await manifestBuilder.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        var knownFiles = plan.Manifest.Files
            .Select(file => Path.GetFullPath(file.TargetPath))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var failures = new List<GameFileRepairFailure>();
        foreach (var reference in FinalLaunchCommandPathReader.Read(startInfo))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizeCommandPath(reference.Path, startInfo.WorkingDirectory);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var exists = reference.IsDirectory ? Directory.Exists(path) : File.Exists(path);
            var insideMinecraftDirectory = IsWithin(path, request.MinecraftDirectory);
            var known = knownFiles.Contains(path);
            if (!exists || (insideMinecraftDirectory && !reference.IsDirectory && !known))
            {
                failures.Add(new GameFileRepairFailure(
                    path,
                    reference.Category,
                    GameFileRepairFailureReason.FinalLaunchPlanInvalid,
                    "None",
                    exists ? "Path was not part of the resolved manifest." : "Referenced path does not exist."));
            }
        }

        if (!string.IsNullOrWhiteSpace(startInfo.FileName) && !File.Exists(startInfo.FileName))
        {
            failures.Add(new GameFileRepairFailure(
                startInfo.FileName,
                "JavaRuntime",
                GameFileRepairFailureReason.FinalLaunchPlanInvalid,
                "None",
                "Java executable does not exist."));
        }

        var result = failures.Count == 0
            ? new GameFileRepairResult(
                LaunchAllowed: true,
                plan.Manifest.Files.Count,
                MissingCount: 0,
                CorruptedCount: 0,
                UnverifiableCount: 0,
                RepairableCount: 0,
                RepairedCount: 0,
                FailedCount: 0,
                Failures: [])
            : new GameFileRepairResult(
                LaunchAllowed: false,
                plan.Manifest.Files.Count,
                failures.Count,
                CorruptedCount: 0,
                UnverifiableCount: 0,
                RepairableCount: 0,
                RepairedCount: 0,
                FailedCount: failures.Count,
                Failures: failures);
        logger.LogInformation(
            "Final launch command validated. VersionName={VersionName} FinalCommandMissingPathCount={FinalCommandMissingPathCount} LaunchAllowed={LaunchAllowed}",
            request.VersionName,
            failures.Count,
            result.LaunchAllowed);
        return result;
    }

    private async Task<GameFileRepairResult> ValidateAsync(
        GameFileIntegrityRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await manifestBuilder.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        var report = await GameFileManifestValidator.ValidateAsync(plan.Manifest, cancellationToken).ConfigureAwait(false);
        return new GameFileRepairResult(
            report.Failures.Count == 0,
            plan.Manifest.Files.Count,
            report.MissingCount,
            report.CorruptedCount,
            report.UnverifiableCount,
            report.RepairableCount,
            RepairedCount: 0,
            report.Failures.Count,
            report.Failures);
    }

    private static GameFileRepairFailureReason ClassifyFailure(InstanceRepairException exception)
    {
        var message = exception.Message;
        if (message.Contains("processor", StringComparison.OrdinalIgnoreCase))
            return GameFileRepairFailureReason.ProcessorRegenerationFailed;
        if (message.Contains("download", StringComparison.OrdinalIgnoreCase))
            return GameFileRepairFailureReason.DownloadFailed;
        if (message.Contains("missing", StringComparison.OrdinalIgnoreCase))
            return GameFileRepairFailureReason.Missing;
        return GameFileRepairFailureReason.Corrupted;
    }

    private static GameFileRepairResult FailureResult(
        GameFileIntegrityRequest request,
        GameFileRepairFailureReason reason,
        string category,
        string recoveryMethod,
        string? source)
    {
        return new GameFileRepairResult(
            LaunchAllowed: false,
            RequiredCount: 0,
            MissingCount: reason == GameFileRepairFailureReason.Missing ? 1 : 0,
            CorruptedCount: reason == GameFileRepairFailureReason.Corrupted ? 1 : 0,
            UnverifiableCount: reason == GameFileRepairFailureReason.MetadataIncomplete ? 1 : 0,
            RepairableCount: 0,
            RepairedCount: 0,
            FailedCount: 1,
            Failures: [new GameFileRepairFailure(request.VersionName, category, reason, recoveryMethod, source)]);
    }

    private void LogReport(string message, GameFileIntegrityRequest request, GameFileRepairResult result, int repairedCount)
    {
        logger.LogInformation(
            "{Message} VersionName={VersionName} RequiredCount={RequiredCount} MissingCount={MissingCount} CorruptedCount={CorruptedCount} UnverifiableCount={UnverifiableCount} RepairableCount={RepairableCount} RepairedCount={RepairedCount} FailedCount={FailedCount} LaunchAllowed={LaunchAllowed}",
            message,
            request.VersionName,
            result.RequiredCount,
            result.MissingCount,
            result.CorruptedCount,
            result.UnverifiableCount,
            result.RepairableCount,
            repairedCount,
            result.FailedCount,
            result.LaunchAllowed);
    }

    private static string NormalizeCommandPath(string path, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return Path.GetFullPath(path, string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory);
    }

    private static bool IsWithin(string candidate, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.Equals(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

internal sealed record ResolvedLaunchPlan(string VersionName, JsonObject VersionJson, RequiredGameFileManifest Manifest);

internal sealed record RequiredGameFileManifest(IReadOnlyList<RequiredGameFile> Files);

internal sealed record RequiredGameFile(
    string TargetPath,
    string Category,
    string? Source,
    string? Sha1,
    long? Size,
    bool Required,
    string RecoveryMethod,
    string Reason);

internal sealed record GameFileValidationReport(
    int MissingCount,
    int CorruptedCount,
    int UnverifiableCount,
    int RepairableCount,
    IReadOnlyList<GameFileRepairFailure> Failures);

internal sealed record GameFileRepairPlan(IReadOnlyList<RequiredGameFile> FilesToRepair);

internal sealed class RequiredGameFileManifestBuilder
{
    private readonly ILogger logger;

    public RequiredGameFileManifestBuilder(ILogger? logger = null)
    {
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task<ResolvedLaunchPlan> ResolveAsync(GameFileIntegrityRequest request, CancellationToken cancellationToken)
    {
        var versionDirectory = Path.Combine(request.MinecraftDirectory, "versions", request.VersionName);
        var versionJson = await ReadResolvedVersionJsonAsync(request.MinecraftDirectory, request.VersionName, versionDirectory, cancellationToken)
            .ConfigureAwait(false);
        var files = new Dictionary<string, RequiredGameFile>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Add(files, new RequiredGameFile(
            Path.Combine(versionDirectory, $"{request.VersionName}.json"),
            "VersionMetadata", null, null, null, true, "Unavailable", "Resolved version metadata"));
        AddClientJar(files, request.MinecraftDirectory, request.VersionName, versionJson);
        AddLibraries(files, request.MinecraftDirectory, versionJson);
        await AddAssetsAsync(files, request.MinecraftDirectory, versionJson, cancellationToken).ConfigureAwait(false);
        AddLogging(files, request.MinecraftDirectory, versionJson);
        return new ResolvedLaunchPlan(request.VersionName, versionJson, new RequiredGameFileManifest(files.Values.ToList()));
    }

    private static async Task<JsonObject> ReadResolvedVersionJsonAsync(
        string minecraftDirectory,
        string versionName,
        string versionDirectory,
        CancellationToken cancellationToken)
    {
        var current = await ReadJsonAsync(Path.Combine(versionDirectory, $"{versionName}.json"), cancellationToken).ConfigureAwait(false);
        var chain = new Stack<JsonObject>();
        chain.Push(current);
        var parentName = GetString(current["inheritsFrom"]);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { versionName };
        while (!string.IsNullOrWhiteSpace(parentName))
        {
            if (!visited.Add(parentName))
                throw new InvalidDataException($"Version inheritance cycle detected at {parentName}.");
            var parentPath = Path.Combine(minecraftDirectory, "versions", parentName, $"{parentName}.json");
            if (!File.Exists(parentPath))
                break;
            var parent = await ReadJsonAsync(parentPath, cancellationToken).ConfigureAwait(false);
            chain.Push(parent);
            parentName = GetString(parent["inheritsFrom"]);
        }

        var resolved = (JsonObject)chain.Pop().DeepClone();
        while (chain.Count > 0)
            resolved = VersionJsonMergeHelper.MergeFlattenedVersion(resolved, chain.Pop(), versionName);
        return resolved;
    }

    private static async Task<JsonObject> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject
            ?? throw new InvalidDataException($"Version metadata is empty: {path}");
    }

    private static void AddClientJar(IDictionary<string, RequiredGameFile> files, string minecraftDirectory, string versionName, JsonObject versionJson)
    {
        var client = versionJson["downloads"]?["client"] as JsonObject;
        Add(files, new RequiredGameFile(
            Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.jar"),
            "ClientJar",
            GetString(client?["url"]),
            GetString(client?["sha1"]),
            GetLong(client?["size"]),
            true,
            string.IsNullOrWhiteSpace(GetString(client?["url"])) ? "Unavailable" : "DirectDownload",
            "Final version client jar"));
    }

    private static void AddLibraries(IDictionary<string, RequiredGameFile> files, string minecraftDirectory, JsonObject versionJson)
    {
        if (versionJson["libraries"] is not JsonArray libraries)
            return;
        var librariesRoot = Path.Combine(minecraftDirectory, "libraries");
        foreach (var library in libraries.OfType<JsonObject>())
        {
            if (!ManagedLibraryArtifactResolver.IsAllowed(library))
                continue;
            foreach (var artifact in ManagedLibraryArtifactResolver.EnumerateDownloads(library))
            {
                var target = MinecraftPathGuard.EnsureWithin(
                    Path.Combine(librariesRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    librariesRoot,
                    "Resolved library");
                Add(files, new RequiredGameFile(
                    target,
                    "Library",
                    artifact.Url,
                    artifact.Sha1,
                    artifact.Size,
                    true,
                    string.IsNullOrWhiteSpace(artifact.Url) ? "Unavailable" : "DirectDownload",
                    artifact.LibraryName ?? artifact.RelativePath));
            }
        }
    }

    private static async Task AddAssetsAsync(
        IDictionary<string, RequiredGameFile> files,
        string minecraftDirectory,
        JsonObject versionJson,
        CancellationToken cancellationToken)
    {
        if (versionJson["assetIndex"] is not JsonObject assetIndex)
            return;
        var id = GetString(assetIndex["id"]);
        // Mojang asset index identifiers commonly contain dots (for example
        // "1.18"), so this is a file-name safety check rather than an
        // extension check.
        if (string.IsNullOrWhiteSpace(id) || Path.GetFileName(id) != id || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Asset index id is invalid.");
        var indexesRoot = Path.Combine(minecraftDirectory, "assets", "indexes");
        var indexPath = MinecraftPathGuard.EnsureWithin(Path.Combine(indexesRoot, $"{id}.json"), indexesRoot, "Asset index");
        Add(files, new RequiredGameFile(indexPath, "AssetIndex", GetString(assetIndex["url"]), GetString(assetIndex["sha1"]), GetLong(assetIndex["size"]), true, "DirectDownload", "Version asset index"));
        if (!File.Exists(indexPath))
            return;

        JsonObject index;
        try
        {
            index = await ReadJsonAsync(indexPath, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Asset index cannot be parsed: {indexPath}", exception);
        }

        if (index["objects"] is not JsonObject objects)
            return;
        var objectsRoot = Path.Combine(minecraftDirectory, "assets", "objects");
        foreach (var objectEntry in objects)
        {
            if (objectEntry.Value is not JsonObject asset)
                continue;
            var hash = GetString(asset["hash"]);
            if (hash is null || !MinecraftFileIntegrity.IsSha1(hash))
                continue;
            var target = MinecraftPathGuard.EnsureWithin(Path.Combine(objectsRoot, hash[..2], hash), objectsRoot, "Asset object");
            Add(files, new RequiredGameFile(target, "AssetObject", $"https://resources.download.minecraft.net/{hash[..2]}/{hash}", hash, GetLong(asset["size"]), true, "DirectDownload", objectEntry.Key));
        }
    }

    private static void AddLogging(IDictionary<string, RequiredGameFile> files, string minecraftDirectory, JsonObject versionJson)
    {
        if (versionJson["logging"]?["client"]?["file"] is not JsonObject logging)
            return;
        var id = GetString(logging["id"]);
        if (string.IsNullOrWhiteSpace(id) || Path.GetFileName(id) != id || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Logging configuration id is invalid.");
        var root = Path.Combine(minecraftDirectory, "assets", "log_configs");
        Add(files, new RequiredGameFile(
            MinecraftPathGuard.EnsureWithin(Path.Combine(root, id), root, "Logging configuration"),
            "LoggingConfiguration",
            GetString(logging["url"]),
            GetString(logging["sha1"]),
            GetLong(logging["size"]),
            true,
            "DirectDownload",
            "Client logging configuration"));
    }

    private static void Add(IDictionary<string, RequiredGameFile> files, RequiredGameFile file)
    {
        var path = Path.GetFullPath(file.TargetPath);
        if (files.TryGetValue(path, out var existing))
        {
            if (!string.IsNullOrWhiteSpace(existing.Sha1)
                && !string.IsNullOrWhiteSpace(file.Sha1)
                && !string.Equals(existing.Sha1, file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Conflicting checksums were declared for {path}.");
            }
            if (string.IsNullOrWhiteSpace(existing.Sha1) && !string.IsNullOrWhiteSpace(file.Sha1))
                files[path] = file;
            return;
        }
        files[path] = file;
    }

    private static string? GetString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? GetLong(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
}

internal static class GameFileManifestValidator
{
    public static async Task<GameFileValidationReport> ValidateAsync(RequiredGameFileManifest manifest, CancellationToken cancellationToken)
    {
        var failures = new List<GameFileRepairFailure>();
        var missing = 0;
        var corrupted = 0;
        var unverifiable = 0;
        var repairable = 0;
        foreach (var file in manifest.Files.Where(file => file.Required))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await MinecraftFileIntegrity.EvaluateAsync(file.TargetPath, file.Sha1, file.Size, MinecraftFileVerification.Full, cancellationToken).ConfigureAwait(false);
            if (status == MinecraftFileIntegrityStatus.Valid && IsOrdinaryFile(file.TargetPath))
            {
                if (string.IsNullOrWhiteSpace(file.Sha1) && file.Size is null)
                    unverifiable++;
                continue;
            }

            var reason = status == MinecraftFileIntegrityStatus.Missing
                ? GameFileRepairFailureReason.Missing
                : GameFileRepairFailureReason.Corrupted;
            if (reason == GameFileRepairFailureReason.Missing)
                missing++;
            else
                corrupted++;
            if (!string.Equals(file.RecoveryMethod, "Unavailable", StringComparison.Ordinal))
                repairable++;
            failures.Add(new GameFileRepairFailure(file.TargetPath, file.Category, reason, file.RecoveryMethod, file.Source));
        }
        return new GameFileValidationReport(missing, corrupted, unverifiable, repairable, failures);
    }

    private static bool IsOrdinaryFile(string path)
    {
        if (!File.Exists(path))
            return false;
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

internal sealed record FinalLaunchCommandPath(string Path, string Category, bool IsDirectory);

internal static partial class FinalLaunchCommandPathReader
{
    private static readonly HashSet<string> PathListOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "-cp", "-classpath", "--class-path", "--module-path", "-p"
    };

    public static IEnumerable<FinalLaunchCommandPath> Read(ProcessStartInfo startInfo)
    {
        var arguments = startInfo.ArgumentList.Count > 0
            ? startInfo.ArgumentList.ToList()
            : Tokenize(startInfo.Arguments).ToList();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (PathListOptions.Contains(argument) && index + 1 < arguments.Count)
            {
                foreach (var path in arguments[++index].Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    yield return new FinalLaunchCommandPath(path, "Classpath", false);
                continue;
            }
            if (argument.StartsWith("-javaagent:", StringComparison.OrdinalIgnoreCase))
            {
                var value = argument["-javaagent:".Length..];
                var separator = value.IndexOf('=');
                yield return new FinalLaunchCommandPath(separator < 0 ? value : value[..separator], "JavaAgent", false);
                continue;
            }
            const string nativePrefix = "-Djava.library.path=";
            if (argument.StartsWith(nativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var path in argument[nativePrefix.Length..].Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    yield return new FinalLaunchCommandPath(path, "NativeDirectory", true);
                continue;
            }
            if (argument.StartsWith("-Dlog4j.configurationFile=", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("-Dlog4j2.configurationFile=", StringComparison.OrdinalIgnoreCase))
            {
                var value = argument[(argument.IndexOf('=') + 1)..];
                yield return new FinalLaunchCommandPath(value, "LoggingConfiguration", false);
            }
        }
    }

    private static IEnumerable<string> Tokenize(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return [];
        return ArgumentRegex().Matches(arguments)
            .Select(match => match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value);
    }

    [GeneratedRegex("(?:\\\"(?<quoted>[^\\\"]*)\\\")|(?<plain>\\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentRegex();
}
