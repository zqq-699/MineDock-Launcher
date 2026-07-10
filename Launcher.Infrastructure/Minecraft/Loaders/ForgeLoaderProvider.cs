using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal interface IForgeInstallerRunner
{
    Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken);
}

internal interface IFinalVersionInstaller
{
    Task InstallAsync(
        string gameDirectory,
        string versionName,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0);
}

internal sealed class FinalVersionInstaller : IFinalVersionInstaller
{
    public async Task InstallAsync(
        string gameDirectory,
        string versionName,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var launcher = VanillaLoaderProvider.CreateLauncher(
            gameDirectory,
            progress,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond: downloadSpeedLimitMbPerSecond);
        VanillaLoaderProvider.AttachProgress(launcher, progress);
        await launcher.InstallAsync(versionName, cancellationToken);
    }
}

internal sealed class ForgeInstallerRunner : IForgeInstallerRunner
{
    internal const string InstallClientUnrecognizedOptionMessage = "'installClient' is not a recognized option";

    public async Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(javaCommand) ? "java" : javaCommand,
                Arguments = $"-jar \"{installerJarPath}\" --installClient \"{minecraftDirectory}\"",
                WorkingDirectory = minecraftDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Forge installer could not be started.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode == 0)
                return;

            var details = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? $"Forge installer exited with code {process.ExitCode}."
                    : $"Forge installer exited with code {process.ExitCode}: {details.Trim()}");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("No usable Java runtime was found for Forge installation.", exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException("No usable Java runtime was found for Forge installation.", exception);
        }
    }
}

public sealed class ForgeLoaderProvider : ILoaderProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex InstallerUrlRegex = new(
        @"https://maven\.minecraftforge\.net/net/minecraftforge/forge/(?<fullVersion>[^""'<>\s]+)/forge-(?<artifactVersion>[^""'<>\s]+)-installer\.jar",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient httpClient;
    private readonly IForgeInstallerRunner installerRunner;
    private readonly IFinalVersionInstaller finalVersionInstaller;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly ILogger logger;
    private readonly string tempRootDirectory;
    private readonly SemaphoreSlim catalogLock = new(1, 1);
    private readonly Dictionary<string, IReadOnlyDictionary<string, ForgeCatalogEntry>> catalogCache = new(StringComparer.OrdinalIgnoreCase);

    public ForgeLoaderProvider(
        HttpClient? httpClient = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<ForgeLoaderProvider>? logger = null)
        : this(httpClient, installerRunner: null, finalVersionInstaller: null, tempRootDirectory: null, downloadSpeedLimitState, logger)
    {
    }

    internal ForgeLoaderProvider(
        HttpClient? httpClient,
        IForgeInstallerRunner? installerRunner,
        string? tempRootDirectory,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null)
        : this(httpClient, installerRunner, finalVersionInstaller: null, tempRootDirectory, downloadSpeedLimitState, logger: null)
    {
    }

    internal ForgeLoaderProvider(
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

    public LoaderKind Kind => LoaderKind.Forge;

    public bool IsImplemented => true;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        CancellationToken cancellationToken = default,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var catalog = await GetCatalogAsync(
            minecraftVersion,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
        return catalog.Values
            .OrderByDescending(entry => ParseForgeVersion(entry.ForgeVersion))
            .ThenByDescending(entry => entry.ForgeVersion, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new LoaderVersionInfo(entry.ForgeVersion))
            .ToList();
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
            throw new InvalidOperationException($"No Forge loader version available for {minecraftVersion}.");

        var catalog = await GetCatalogAsync(
            minecraftVersion,
            downloadSourcePreference,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
        if (!catalog.TryGetValue(selectedLoaderVersion, out var catalogEntry))
            throw new InvalidOperationException($"Forge loader version {selectedLoaderVersion} is not available for {minecraftVersion}.");

        var existingVersionNames = GetVersionDirectoryNames(gameDirectory);
        var installerSessionDirectory = Path.Combine(tempRootDirectory, "launcher-forge", Guid.NewGuid().ToString("N"));
        var installerJarPath = Path.Combine(installerSessionDirectory, $"forge-{minecraftVersion}-{selectedLoaderVersion}-installer.jar");
        var installerMinecraftDirectory = Path.Combine(installerSessionDirectory, ".minecraft");
        Directory.CreateDirectory(installerSessionDirectory);

        try
        {
            EnsureLauncherProfileExists(installerMinecraftDirectory);

            progress?.Report(new LauncherProgress(InstallProgressStages.DownloadingLoaderInstaller, string.Empty));
            await DownloadInstallerAsync(
                catalogEntry.InstallerUrl,
                installerJarPath,
                downloadSourcePreference,
                cancellationToken,
                downloadSpeedLimitMbPerSecond);

            progress?.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));
            try
            {
                await installerRunner.RunInstallerAsync("java", installerJarPath, installerMinecraftDirectory, cancellationToken);
            }
            catch (InvalidOperationException exception) when (IsLegacyForgeInstallClientFailure(exception))
            {
                logger.LogInformation(
                    exception,
                    "Legacy Forge installer detected because --installClient is unsupported. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion}",
                    minecraftVersion,
                    selectedLoaderVersion);
                await InstallLegacyForgeClientAsync(
                    installerJarPath,
                    installerMinecraftDirectory,
                    minecraftVersion,
                    selectedLoaderVersion,
                    downloadSourcePreference,
                    progress,
                    cancellationToken,
                    downloadSpeedLimitMbPerSecond);
            }

            var sourceVersionName = FindInstalledSourceVersionName(
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                []);

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

            CopyFinalVersionDirectory(installerMinecraftDirectory, gameDirectory, finalVersionName);
            MinecraftSharedContentCopier.CopySharedRuntimeContent(installerMinecraftDirectory, gameDirectory, logger);

            CleanupCreatedVersionDirectories(gameDirectory, existingVersionNames, finalVersionName);
            return finalVersionName;
        }
        catch
        {
            CleanupCreatedVersionDirectories(gameDirectory, existingVersionNames, preserveVersionName: null);
            throw;
        }
        finally
        {
            TryDeleteDirectory(installerSessionDirectory);
        }
    }

    private async Task<IReadOnlyDictionary<string, ForgeCatalogEntry>> GetCatalogAsync(
        string minecraftVersion,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        await catalogLock.WaitAsync(cancellationToken);
        try
        {
            var cacheKey = $"{downloadSourcePreference}:{minecraftVersion}";
            if (catalogCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var executor = new MinecraftDownloadRequestExecutor(
                httpClient,
                logger,
                DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
                category: DownloadConcurrencyCategory.Metadata);
            var result = await executor.ExecuteLookupAsync(
                GetForgeIndexUrl(minecraftVersion),
                downloadSourcePreference,
                categoryHint: "Forge",
                async (context, token) =>
                {
                    IReadOnlyDictionary<string, ForgeCatalogEntry> entries;
                    if (context.Resolution.ResolvedSourceKind.Equals("BmclApiForge", StringComparison.Ordinal))
                    {
                        await using var stream = await context.Response.Content.ReadAsStreamAsync(token);
                        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                        if (document.RootElement.ValueKind is not JsonValueKind.Array)
                        {
                            throw new DownloadContentValidationException(
                                "BMCLAPI Forge catalog is not a JSON array.");
                        }

                        entries = ParseBmclCatalogEntries(minecraftVersion, document.RootElement);
                        if (document.RootElement.GetArrayLength() > 0 && entries.Count == 0)
                        {
                            throw new DownloadContentValidationException(
                                "BMCLAPI Forge catalog contains no valid loader entries.");
                        }
                    }
                    else
                    {
                        var html = await context.Response.Content.ReadAsStringAsync(token);
                        if (!html.Contains("<html", StringComparison.OrdinalIgnoreCase)
                            && !html.Contains("<!doctype", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new DownloadContentValidationException(
                                "Forge catalog response is not an HTML document.");
                        }

                        entries = ParseCatalogEntries(minecraftVersion, html);
                    }

                    if (entries.Count == 0)
                        throw new DownloadNoResultException("Forge returned no matching loader versions.");

                    return entries;
                },
                statusCode => statusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
                cancellationToken);
            catalogCache[cacheKey] = result.Found ? result.Value! : EmptyCatalog();
            return catalogCache[cacheKey];
        }
        finally
        {
            catalogLock.Release();
        }
    }

    private static IReadOnlyDictionary<string, ForgeCatalogEntry> ParseCatalogEntries(string minecraftVersion, string html)
    {
        var entries = new Dictionary<string, ForgeCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in InstallerUrlRegex.Matches(html))
        {
            var fullVersion = match.Groups["fullVersion"].Value;
            var artifactVersion = match.Groups["artifactVersion"].Value;
            if (!string.Equals(fullVersion, artifactVersion, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!fullVersion.StartsWith($"{minecraftVersion}-", StringComparison.OrdinalIgnoreCase))
                continue;

            var forgeVersion = fullVersion[(minecraftVersion.Length + 1)..];
            if (string.IsNullOrWhiteSpace(forgeVersion))
                continue;

            if (entries.ContainsKey(forgeVersion))
                continue;

            entries[forgeVersion] = new ForgeCatalogEntry(
                minecraftVersion,
                forgeVersion,
                new Uri(match.Value, UriKind.Absolute));
        }

        return entries;
    }

    private static Version ParseForgeVersion(string version)
    {
        return Version.TryParse(version, out var parsed)
            ? parsed
            : new Version(0, 0);
    }

    private static string GetForgeIndexUrl(string minecraftVersion)
    {
        return $"https://files.minecraftforge.net/net/minecraftforge/forge/index_{minecraftVersion}.html";
    }

    private static IReadOnlyDictionary<string, ForgeCatalogEntry> EmptyCatalog()
    {
        return new Dictionary<string, ForgeCatalogEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task DownloadInstallerAsync(
        Uri installerUrl,
        string destinationPath,
        DownloadSourcePreference downloadSourcePreference,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        var executor = new MinecraftDownloadRequestExecutor(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(downloadSpeedLimitMbPerSecond, downloadSpeedLimitState),
            category: DownloadConcurrencyCategory.Runtime);
        await executor.DownloadFileAsync(
            installerUrl.AbsoluteUri,
            downloadSourcePreference,
            categoryHint: "Forge",
            destinationPath,
            expectedSha1: null,
            expectedSize: null,
            reportDownloadedBytes: null,
            cancellationToken);
    }

    private static HashSet<string> GetVersionDirectoryNames(string gameDirectory)
    {
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return [];

        return Directory.GetDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureLauncherProfileExists(string gameDirectory)
    {
        Directory.CreateDirectory(gameDirectory);

        var launcherProfilesPath = Path.Combine(gameDirectory, "launcher_profiles.json");
        var microsoftStoreProfilesPath = Path.Combine(gameDirectory, "launcher_profiles_microsoft_store.json");
        if (File.Exists(launcherProfilesPath) || File.Exists(microsoftStoreProfilesPath))
            return;

        File.WriteAllText(
            launcherProfilesPath,
            """
            {
              "profiles": {}
            }
            """);
    }

    private static string FindInstalledSourceVersionName(
        string gameDirectory,
        string minecraftVersion,
        string forgeVersion,
        HashSet<string> existingVersionNames)
    {
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            throw new InvalidOperationException("Forge installation did not create a version directory.");

        var candidates = Directory.GetDirectories(versionsDirectory)
            .Select(directory => new DirectoryInfo(directory))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ToList();

        var sourceVersion = candidates
            .Where(directory => !existingVersionNames.Contains(directory.Name))
            .Select(directory => TryCreateSourceMatch(directory.FullName, directory.Name, minecraftVersion, forgeVersion))
            .FirstOrDefault(match => match is not null)
            ?? candidates
                .Select(directory => TryCreateSourceMatch(directory.FullName, directory.Name, minecraftVersion, forgeVersion))
                .FirstOrDefault(match => match is not null);

        return sourceVersion?.VersionName
            ?? throw new InvalidOperationException($"Forge installer did not produce a usable version for {minecraftVersion}-{forgeVersion}.");
    }

    private static ForgeSourceMatch? TryCreateSourceMatch(
        string versionDirectory,
        string versionName,
        string minecraftVersion,
        string forgeVersion)
    {
        var metadata = TryReadVersionMetadata(versionDirectory, versionName);
        if (metadata is null)
            return null;

        var combinedVersion = $"{minecraftVersion}-{forgeVersion}";
        var hasExactForgeLibrary = metadata.LibraryNames.Any(library =>
            library.Contains($"net.minecraftforge:forge:{combinedVersion}", StringComparison.OrdinalIgnoreCase));
        var normalizedMetadata = $"{metadata.Id} {metadata.InheritsFrom} {metadata.Jar} {versionName}";
        var hasLooseForgeMatch = normalizedMetadata.Contains("forge", StringComparison.OrdinalIgnoreCase)
            && normalizedMetadata.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase)
            && normalizedMetadata.Contains(forgeVersion, StringComparison.OrdinalIgnoreCase);

        if (!hasExactForgeLibrary && !hasLooseForgeMatch)
            return null;

        return new ForgeSourceMatch(versionName, metadata);
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
            ?? throw new InvalidOperationException($"Forge version metadata is missing for {sourceVersionName}.");

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
                await WriteLauncherMetadataAsync(gameDirectory, finalVersionName, minecraftVersion, cancellationToken);
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
        await WriteLauncherMetadataAsync(gameDirectory, finalVersionName, minecraftVersion, cancellationToken);
        return finalVersionName;
    }

    private static async Task WriteLauncherMetadataAsync(
        string gameDirectory,
        string versionName,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        var versionJsonPath = Path.Combine(gameDirectory, "versions", versionName, $"{versionName}.json");
        JsonNode versionJson;
        await using (var jsonStream = File.OpenRead(versionJsonPath))
        {
            versionJson = await JsonNode.ParseAsync(jsonStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException($"Version JSON is empty: {versionJsonPath}");
        }

        var versionObject = versionJson.AsObject();
        LauncherVersionMetadata.Apply(versionObject, minecraftVersion);
        await File.WriteAllTextAsync(
            versionJsonPath,
            versionObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
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

    private static IReadOnlyDictionary<string, ForgeCatalogEntry> ParseBmclCatalogEntries(
        string minecraftVersion,
        JsonElement root)
    {
        var entries = new Dictionary<string, ForgeCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("version", out var versionProperty)
                || versionProperty.ValueKind is not JsonValueKind.String)
            {
                continue;
            }

            var forgeVersion = versionProperty.GetString();
            if (string.IsNullOrWhiteSpace(forgeVersion) || entries.ContainsKey(forgeVersion))
                continue;

            var installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{minecraftVersion}-{forgeVersion}/forge-{minecraftVersion}-{forgeVersion}-installer.jar";
            entries[forgeVersion] = new ForgeCatalogEntry(
                minecraftVersion,
                forgeVersion,
                new Uri(installerUrl, UriKind.Absolute));
        }

        return entries;
    }

    private static bool IsLegacyForgeInstallClientFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(ForgeInstallerRunner.InstallClientUnrecognizedOptionMessage, StringComparison.OrdinalIgnoreCase)
                || (current.Message.Contains("installClient", StringComparison.OrdinalIgnoreCase)
                    && current.Message.Contains("UnrecognizedOptionException", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private async Task InstallLegacyForgeClientAsync(
        string installerJarPath,
        string gameDirectory,
        string minecraftVersion,
        string forgeVersion,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond)
    {
        logger.LogInformation(
            "Legacy Forge fallback started. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion}",
            minecraftVersion,
            forgeVersion);

        var profile = await ReadLegacyForgeInstallProfileAsync(installerJarPath, minecraftVersion, forgeVersion, cancellationToken);

        await finalVersionInstaller.InstallAsync(
            gameDirectory,
            minecraftVersion,
            downloadSourcePreference,
            progress,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);

        WriteLegacyForgeVersionMetadata(gameDirectory, profile);
        ExtractLegacyForgeLibrary(installerJarPath, gameDirectory, profile);

        logger.LogInformation(
            "Legacy Forge fallback completed. MinecraftVersion={MinecraftVersion} LoaderVersion={LoaderVersion} SourceVersionName={SourceVersionName}",
            minecraftVersion,
            forgeVersion,
            profile.SourceVersionName);
    }

    private async Task<LegacyForgeInstallProfile> ReadLegacyForgeInstallProfileAsync(
        string installerJarPath,
        string minecraftVersion,
        string forgeVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(installerJarPath);
            var profileEntry = archive.GetEntry("install_profile.json")
                ?? throw new InvalidDataException("Legacy Forge installer is missing install_profile.json.");

            await using var profileStream = profileEntry.Open();
            var profileNode = await JsonNode.ParseAsync(profileStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("Legacy Forge install_profile.json is empty.");
            var profileObject = profileNode.AsObject();
            var installObject = profileObject["install"] as JsonObject
                ?? throw new InvalidDataException("Legacy Forge install_profile.json is missing install.");
            var versionInfo = profileObject["versionInfo"] as JsonObject
                ?? throw new InvalidDataException("Legacy Forge install_profile.json is missing versionInfo.");

            var sourceVersionName = GetRequiredString(versionInfo, "id", "versionInfo.id");
            var installMinecraftVersion = GetStringProperty(installObject, "minecraft");
            if (!string.IsNullOrWhiteSpace(installMinecraftVersion)
                && !string.Equals(installMinecraftVersion, minecraftVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Legacy Forge installer targets Minecraft {installMinecraftVersion}, not {minecraftVersion}.");
            }

            var forgeLibraryCoordinate = GetRequiredString(installObject, "path", "install.path");
            if (!forgeLibraryCoordinate.Contains($":{minecraftVersion}-{forgeVersion}", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Legacy Forge installer path does not match {minecraftVersion}-{forgeVersion}.");
            }

            var installerFilePath = GetRequiredString(installObject, "filePath", "install.filePath");
            if (archive.GetEntry(installerFilePath) is null)
                throw new FileNotFoundException($"Legacy Forge installer payload is missing: {installerFilePath}", installerFilePath);

            return new LegacyForgeInstallProfile(
                sourceVersionName,
                forgeLibraryCoordinate,
                installerFilePath,
                (JsonObject)versionInfo.DeepClone());
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not InvalidDataException and not FileNotFoundException)
        {
            throw new InvalidDataException("Legacy Forge installer profile could not be read.", exception);
        }
    }

    private void WriteLegacyForgeVersionMetadata(string gameDirectory, LegacyForgeInstallProfile profile)
    {
        var versionDirectory = Path.Combine(gameDirectory, "versions", profile.SourceVersionName);
        var versionJsonPath = Path.Combine(versionDirectory, $"{profile.SourceVersionName}.json");
        if (Directory.Exists(versionDirectory))
            throw new IOException($"Legacy Forge source version directory already exists: {profile.SourceVersionName}");

        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(versionJsonPath, profile.VersionInfo.ToJsonString(JsonOptions));

        logger.LogInformation(
            "Legacy Forge version metadata written. SourceVersionName={SourceVersionName} VersionJsonPath={VersionJsonPath}",
            profile.SourceVersionName,
            versionJsonPath);
    }

    private void ExtractLegacyForgeLibrary(string installerJarPath, string gameDirectory, LegacyForgeInstallProfile profile)
    {
        if (!TryBuildMavenArtifactPath(profile.ForgeLibraryCoordinate, out var relativeArtifactPath))
            throw new InvalidDataException($"Legacy Forge library coordinate is invalid: {profile.ForgeLibraryCoordinate}");

        var libraryPath = Path.Combine(
            gameDirectory,
            "libraries",
            relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);

        using var archive = ZipFile.OpenRead(installerJarPath);
        var payloadEntry = archive.GetEntry(profile.InstallerFilePath)
            ?? throw new FileNotFoundException($"Legacy Forge installer payload is missing: {profile.InstallerFilePath}", profile.InstallerFilePath);

        using var source = payloadEntry.Open();
        using var destination = File.Create(libraryPath);
        source.CopyTo(destination);

        logger.LogInformation(
            "Legacy Forge library extracted. Coordinate={Coordinate} LibraryPath={LibraryPath}",
            profile.ForgeLibraryCoordinate,
            libraryPath);
    }

    private static bool TryBuildMavenArtifactPath(string mavenName, out string relativePath)
    {
        relativePath = string.Empty;
        var parts = mavenName.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || parts.Length > 4)
            return false;

        var extension = "jar";
        var versionAndExtension = parts[2].Split('@', 2, StringSplitOptions.TrimEntries);
        var version = versionAndExtension[0];
        if (string.IsNullOrWhiteSpace(version))
            return false;

        if (versionAndExtension.Length == 2 && !string.IsNullOrWhiteSpace(versionAndExtension[1]))
            extension = versionAndExtension[1];

        string? classifier = null;
        if (parts.Length == 4)
        {
            var classifierAndExtension = parts[3].Split('@', 2, StringSplitOptions.TrimEntries);
            classifier = classifierAndExtension[0];
            if (classifierAndExtension.Length == 2 && !string.IsNullOrWhiteSpace(classifierAndExtension[1]))
                extension = classifierAndExtension[1];
        }

        var groupPath = parts[0].Replace('.', '/');
        var artifact = parts[1];
        if (string.IsNullOrWhiteSpace(groupPath) || string.IsNullOrWhiteSpace(artifact))
            return false;

        var fileName = string.IsNullOrWhiteSpace(classifier)
            ? $"{artifact}-{version}.{extension}"
            : $"{artifact}-{version}-{classifier}.{extension}";
        relativePath = $"{groupPath}/{artifact}/{version}/{fileName}";
        return true;
    }

    private static string GetRequiredString(JsonObject node, string propertyName, string displayName)
    {
        var value = GetStringProperty(node, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Legacy Forge install_profile.json is missing {displayName}.");

        return value;
    }

    private static string GetStringProperty(JsonObject node, string name)
    {
        return node[name]?.GetValue<string>() ?? string.Empty;
    }

    private static void CopyFinalVersionDirectory(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string versionName)
    {
        MinecraftVersionDirectoryCopier.CopyVersionDirectory(
            sourceGameDirectory,
            destinationGameDirectory,
            versionName,
            allowExistingDestination: true);
    }

    private static void CleanupCreatedVersionDirectories(
        string gameDirectory,
        HashSet<string> existingVersionNames,
        string? preserveVersionName)
    {
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return;

        foreach (var directory in Directory.GetDirectories(versionsDirectory))
        {
            var versionName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(versionName)
                || existingVersionNames.Contains(versionName)
                || string.Equals(versionName, preserveVersionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

    private sealed record ForgeCatalogEntry(
        string MinecraftVersion,
        string ForgeVersion,
        Uri InstallerUrl);

    private sealed record ForgeSourceMatch(
        string VersionName,
        VersionJsonMetadata Metadata);

    private sealed record LegacyForgeInstallProfile(
        string SourceVersionName,
        string ForgeLibraryCoordinate,
        string InstallerFilePath,
        JsonObject VersionInfo);

    private sealed record VersionJsonMetadata(
        string Id,
        string InheritsFrom,
        string Jar,
        IReadOnlyList<string> LibraryNames);
}
