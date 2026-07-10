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

using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

public sealed class NeoForgeLoaderProvider : ILoaderProvider
{
    private const string MetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const string ArtifactBaseUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private static readonly Regex MinecraftVersionPattern = new(@"^1\.(?<minor>\d+)\.(?<patch>\d+)$", RegexOptions.Compiled);
    private readonly HttpClient httpClient;
    private readonly IForgeInstallerRunner installerRunner;
    private readonly IFinalVersionInstaller finalVersionInstaller;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;
    private readonly string tempRootDirectory;

    public NeoForgeLoaderProvider(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<NeoForgeLoaderProvider>? logger = null)
        : this(httpClient, installerRunner: null, finalVersionInstaller: null, tempRootDirectory: null, downloadSpeedLimitState, logger)
    {
    }

    internal NeoForgeLoaderProvider(
        HttpClient? httpClient,
        IForgeInstallerRunner? installerRunner,
        string? tempRootDirectory,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null)
        : this(httpClient, installerRunner, finalVersionInstaller: null, tempRootDirectory, downloadSpeedLimitState, logger: null)
    {
    }

    internal NeoForgeLoaderProvider(
        HttpClient? httpClient,
        IForgeInstallerRunner? installerRunner,
        IFinalVersionInstaller? finalVersionInstaller,
        string? tempRootDirectory,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger? logger = null)
    {
        this.httpClient = httpClient ?? MinecraftHttpClientFactory.CreateTransportClient();
        this.installerRunner = installerRunner ?? new ForgeInstallerRunner();
        this.finalVersionInstaller = finalVersionInstaller ?? new FinalVersionInstaller();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? NullLogger.Instance;
        this.tempRootDirectory = string.IsNullOrWhiteSpace(tempRootDirectory) ? Path.GetTempPath() : tempRootDirectory;
    }

    public LoaderKind Kind => LoaderKind.NeoForge;

    public bool IsImplemented => true;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        if (!TryGetNeoForgeVersionPrefix(minecraftVersion, out var versionPrefix))
        {
            logger.LogInformation(
                "Skipping NeoForge version lookup because the Minecraft version is unsupported. MinecraftVersion={MinecraftVersion}",
                minecraftVersion);
            return [];
        }

        logger.LogInformation(
            "Loading NeoForge versions. MinecraftVersion={MinecraftVersion} Prefix={VersionPrefix}",
            minecraftVersion,
            versionPrefix);

        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Metadata);
        var result = await executor.ExecuteLookupAsync(
            MetadataUrl,
            downloadSourcePreference,
            categoryHint: "NeoForge",
            async (context, token) =>
            {
                await using var stream = await context.Response.Content.ReadAsStreamAsync(token);
                var document = await XDocument.LoadAsync(stream, LoadOptions.None, token);
                if (document.Root?.Name.LocalName is not "metadata"
                    || document.Descendants("versions").FirstOrDefault() is null)
                {
                    throw new DownloadContentValidationException(
                        "NeoForge Maven metadata has an invalid structure.");
                }

                var versions = document.Descendants("version")
                    .Select(element => element.Value?.Trim())
                    .Where(version => !string.IsNullOrWhiteSpace(version))
                    .Select(version => version!)
                    .Where(version => version.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(CreateLoaderVersionInfo)
                    .OrderByDescending(info => info.IsStable)
                    .ThenByDescending(info => ParseVersionKey(info.Version), NeoForgeVersionKeyComparer.Instance)
                    .ThenByDescending(info => info.Version, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (versions.Count == 0)
                    throw new DownloadNoResultException("NeoForge returned no matching loader versions.");

                return (IReadOnlyList<LoaderVersionInfo>)versions;
            },
            statusCode => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            cancellationToken);

        var resolvedVersions = result.Found ? result.Value! : [];
        logger.LogInformation(
            "Loaded NeoForge versions. MinecraftVersion={MinecraftVersion} Count={Count}",
            minecraftVersion,
            resolvedVersions.Count);
        return resolvedVersions;
    }

    public async Task<string> InstallAsync(
        string minecraftVersion,
        string gameDirectory,
        string isolatedVersionName,
        string? loaderVersion,
        IProgress<LauncherProgress>? progress,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        progress?.Report(new LauncherProgress(InstallProgressStages.Preparing, string.Empty));

        var selectedLoaderVersion = loaderVersion;
        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            var availableVersions = await GetLoaderVersionsAsync(
                minecraftVersion,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);
            selectedLoaderVersion = availableVersions.FirstOrDefault()?.Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No NeoForge loader version available for {minecraftVersion}.");

        logger.LogInformation(
            "Installing NeoForge. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} TargetVersionName={TargetVersionName}",
            minecraftVersion,
            selectedLoaderVersion,
            isolatedVersionName);

        var existingVersionNames = LoaderVersionDirectoryTransaction.CaptureExistingVersions(gameDirectory);
        var installerSessionDirectory = Path.Combine(tempRootDirectory, "launcher-neoforge", Guid.NewGuid().ToString("N"));
        var installerJarPath = Path.Combine(installerSessionDirectory, $"neoforge-{selectedLoaderVersion}-installer.jar");
        var installerMinecraftDirectory = Path.Combine(installerSessionDirectory, ".minecraft");
        Directory.CreateDirectory(installerSessionDirectory);

        try
        {
            LoaderVersionDirectoryTransaction.EnsureLauncherProfileExists(installerMinecraftDirectory);

            progress?.Report(new LauncherProgress(InstallProgressStages.DownloadingLoaderInstaller, string.Empty));
            await DownloadInstallerAsync(
                selectedLoaderVersion,
                installerJarPath,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);

            progress?.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));
            await installerRunner.RunInstallerAsync("java", installerJarPath, installerMinecraftDirectory, cancellationToken);

            var sourceVersionName = FindInstalledSourceVersionName(
                installerMinecraftDirectory,
                selectedLoaderVersion,
                existingVersionNames);

            progress?.Report(new LauncherProgress(InstallProgressStages.FinalizingVersion, string.Empty));
            var finalVersionName = await CreateFinalVersionAsync(
                installerMinecraftDirectory,
                sourceVersionName,
                isolatedVersionName,
                minecraftVersion,
                cancellationToken);

            await EnsureFinalVersionIsSelfContainedAsync(
                installerMinecraftDirectory,
                finalVersionName,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);

            progress?.Report(new LauncherProgress(InstallProgressStages.CompletingFiles, string.Empty));
            await finalVersionInstaller.InstallAsync(
                installerMinecraftDirectory,
                finalVersionName,
                downloadSourcePreference,
                progress,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);

            LoaderVersionDirectoryTransaction.CopyFinalVersionDirectory(
                installerMinecraftDirectory,
                gameDirectory,
                finalVersionName,
                cancellationToken);
            MinecraftSharedContentCopier.CopySharedRuntimeContent(installerMinecraftDirectory, gameDirectory, logger);

            LoaderVersionDirectoryTransaction.CleanupCreatedVersionDirectories(gameDirectory, existingVersionNames, finalVersionName);
            logger.LogInformation(
                "NeoForge installation completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} FinalVersionName={FinalVersionName}",
                minecraftVersion,
                selectedLoaderVersion,
                finalVersionName);
            return finalVersionName;
        }
        catch
        {
            LoaderVersionDirectoryTransaction.CleanupCreatedVersionDirectories(gameDirectory, existingVersionNames, preserveVersionName: null);
            throw;
        }
        finally
        {
            LoaderVersionDirectoryTransaction.TryDeleteDirectory(installerSessionDirectory);
        }
    }

    private async Task DownloadInstallerAsync(
        string loaderVersion,
        string destinationPath,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond)
    {
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Runtime);
        await executor.DownloadFileAsync(
            $"{ArtifactBaseUrl}/{loaderVersion}/neoforge-{loaderVersion}-installer.jar",
            downloadSourcePreference,
            categoryHint: "NeoForge",
            destinationPath,
            expectedSha1: null,
            expectedSize: null,
            reportDownloadedBytes: null,
            cancellationToken);
    }

    private async Task EnsureFinalVersionIsSelfContainedAsync(
        string gameDirectory,
        string finalVersionName,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var versionDirectory = Path.Combine(gameDirectory, "versions", finalVersionName);
        var repairService = new ManagedVersionRepairService(httpClient, downloadSpeedLimitState, logger);
        await repairService.EnsureVersionIsSelfContainedAsync(
            gameDirectory,
            finalVersionName,
            versionDirectory,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond: downloadSpeedLimitMbPerSecond);
    }

    private static bool TryGetNeoForgeVersionPrefix(string minecraftVersion, out string prefix)
    {
        var match = MinecraftVersionPattern.Match(minecraftVersion);
        if (!match.Success)
        {
            prefix = string.Empty;
            return false;
        }

        prefix = $"{match.Groups["minor"].Value}.{match.Groups["patch"].Value}.";
        return true;
    }

    private static LoaderVersionInfo CreateLoaderVersionInfo(string version)
    {
        return new LoaderVersionInfo(version, IsStableVersion(version));
    }

    private static bool IsStableVersion(string version)
    {
        return version.IndexOf("-beta", StringComparison.OrdinalIgnoreCase) < 0
               && version.IndexOf("-alpha", StringComparison.OrdinalIgnoreCase) < 0
               && version.IndexOf("+snapshot", StringComparison.OrdinalIgnoreCase) < 0
               && version.IndexOf("+pre", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static NeoForgeVersionKey ParseVersionKey(string version)
    {
        var numericPart = version;
        var suffixIndex = version.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            numericPart = version[..suffixIndex];

        var values = numericPart
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, out var value) ? value : 0)
            .ToArray();
        return new NeoForgeVersionKey(values);
    }

    private static string FindInstalledSourceVersionName(
        string gameDirectory,
        string loaderVersion,
        HashSet<string> existingVersionNames)
    {
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            throw new InvalidOperationException("NeoForge installation did not create a version directory.");

        var expectedVersionId = $"neoforge-{loaderVersion}";
        var candidates = Directory.GetDirectories(versionsDirectory)
            .Select(directory => new DirectoryInfo(directory))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ToList();

        var sourceVersion = candidates
            .Where(directory => !existingVersionNames.Contains(directory.Name))
            .Select(directory => TryCreateSourceMatch(directory.FullName, directory.Name, expectedVersionId, loaderVersion))
            .FirstOrDefault(match => match is not null)
            ?? candidates
                .Select(directory => TryCreateSourceMatch(directory.FullName, directory.Name, expectedVersionId, loaderVersion))
                .FirstOrDefault(match => match is not null);

        return sourceVersion?.VersionName
            ?? throw new InvalidOperationException($"NeoForge installer did not produce a usable version for {loaderVersion}.");
    }

    private static NeoForgeSourceMatch? TryCreateSourceMatch(
        string versionDirectory,
        string versionName,
        string expectedVersionId,
        string loaderVersion)
    {
        var metadata = TryReadVersionMetadata(versionDirectory, versionName);
        if (metadata is null)
            return null;

        var hasExactNeoForgeLibrary = metadata.LibraryNames.Any(library =>
            library.Contains($"net.neoforged:neoforge:{loaderVersion}", StringComparison.OrdinalIgnoreCase));
        var normalizedMetadata = $"{metadata.Id} {metadata.InheritsFrom} {metadata.Jar} {versionName}";
        var hasExpectedVersionId = normalizedMetadata.Contains(expectedVersionId, StringComparison.OrdinalIgnoreCase);
        var hasLooseNeoForgeMatch = normalizedMetadata.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
            && normalizedMetadata.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase);

        if (!hasExactNeoForgeLibrary && !hasExpectedVersionId && !hasLooseNeoForgeMatch)
            return null;

        return new NeoForgeSourceMatch(versionName, metadata);
    }

    private static async Task<string> CreateFinalVersionAsync(
        string gameDirectory,
        string sourceVersionName,
        string isolatedVersionName,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var sourceDirectory = Path.Combine(gameDirectory, "versions", sourceVersionName);
        var metadata = TryReadVersionMetadata(sourceDirectory, sourceVersionName)
            ?? throw new InvalidOperationException($"NeoForge version metadata is missing for {sourceVersionName}.");

        string finalVersionName;
        if (!string.IsNullOrWhiteSpace(metadata.InheritsFrom))
        {
            try
            {
                finalVersionName = await VanillaVersionIsolator.CreateFlattenedDerivedVersionAsync(
                    metadata.InheritsFrom,
                    sourceVersionName,
                    isolatedVersionName,
                    gameDirectory,
                    cancellationToken);
                await LoaderVersionDirectoryTransaction.WriteLauncherMetadataAsync(
                    gameDirectory,
                    finalVersionName,
                    minecraftVersion,
                    cancellationToken);
                return finalVersionName;
            }
            catch (FileNotFoundException)
            {
            }
        }

        finalVersionName = await VanillaVersionIsolator.CreateIsolatedVersionFromSourceAsync(
            sourceVersionName,
            isolatedVersionName,
            gameDirectory,
            cancellationToken: cancellationToken);
        await LoaderVersionDirectoryTransaction.WriteLauncherMetadataAsync(
            gameDirectory,
            finalVersionName,
            minecraftVersion,
            cancellationToken);
        return finalVersionName;
    }

    private static VersionJsonMetadata? TryReadVersionMetadata(string versionDirectory, string versionName)
    {
        var versionJsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        if (!File.Exists(versionJsonPath))
            return null;

        try
        {
            using var stream = File.OpenRead(versionJsonPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            return new VersionJsonMetadata(
                GetStringProperty(root, "id"),
                GetStringProperty(root, "inheritsFrom"),
                GetStringProperty(root, "jar"),
                ReadLibraryNames(root));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadLibraryNames(JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var libraries)
            || libraries.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var library in libraries.EnumerateArray())
        {
            var name = GetStringProperty(library, "name");
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }

    private static string GetStringProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property)
               && property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record NeoForgeSourceMatch(string VersionName, VersionJsonMetadata Metadata);

    private sealed record VersionJsonMetadata(
        string Id,
        string InheritsFrom,
        string Jar,
        IReadOnlyList<string> LibraryNames);

    private readonly record struct NeoForgeVersionKey(int[] Parts);

    private sealed class NeoForgeVersionKeyComparer : IComparer<NeoForgeVersionKey>
    {
        public static NeoForgeVersionKeyComparer Instance { get; } = new();

        public int Compare(NeoForgeVersionKey left, NeoForgeVersionKey right)
        {
            var length = Math.Max(left.Parts.Length, right.Parts.Length);
            for (var index = 0; index < length; index++)
            {
                var leftValue = index < left.Parts.Length ? left.Parts[index] : 0;
                var rightValue = index < right.Parts.Length ? right.Parts[index] : 0;
                var comparison = leftValue.CompareTo(rightValue);
                if (comparison != 0)
                    return comparison;
            }

            return 0;
        }
    }
}
