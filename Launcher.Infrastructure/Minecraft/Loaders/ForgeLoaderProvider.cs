using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal interface IForgeInstallerRunner
{
    Task RunInstallerAsync(string javaPath, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken);
}

internal interface IFinalVersionInstaller
{
    Task InstallAsync(
        string gameDirectory,
        string versionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken);
}

internal sealed class FinalVersionInstaller : IFinalVersionInstaller
{
    public async Task InstallAsync(
        string gameDirectory,
        string versionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var launcher = VanillaLoaderProvider.CreateLauncher(gameDirectory, progress);
        VanillaLoaderProvider.AttachProgress(launcher, progress);
        await launcher.InstallAsync(versionName, cancellationToken);
    }
}

internal sealed class ForgeInstallerRunner : IForgeInstallerRunner
{
    public async Task RunInstallerAsync(string javaPath, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath,
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
    private const string ForgePromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private static readonly Regex InstallerUrlRegex = new(
        @"https://maven\.minecraftforge\.net/net/minecraftforge/forge/(?<fullVersion>[^""'<>\s]+)/forge-(?<artifactVersion>[^""'<>\s]+)-installer\.jar",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient httpClient;
    private readonly IForgeInstallerRunner installerRunner;
    private readonly IFinalVersionInstaller finalVersionInstaller;
    private readonly string tempRootDirectory;
    private readonly SemaphoreSlim catalogLock = new(1, 1);
    private readonly Dictionary<string, IReadOnlyDictionary<string, ForgeCatalogEntry>> catalogCache = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? supportedMinecraftVersions;

    public ForgeLoaderProvider(HttpClient? httpClient = null)
        : this(httpClient, installerRunner: null, tempRootDirectory: null)
    {
    }

    internal ForgeLoaderProvider(HttpClient? httpClient, IForgeInstallerRunner? installerRunner, string? tempRootDirectory)
        : this(httpClient, installerRunner, finalVersionInstaller: null, tempRootDirectory)
    {
    }

    internal ForgeLoaderProvider(
        HttpClient? httpClient,
        IForgeInstallerRunner? installerRunner,
        IFinalVersionInstaller? finalVersionInstaller,
        string? tempRootDirectory)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.installerRunner = installerRunner ?? new ForgeInstallerRunner();
        this.finalVersionInstaller = finalVersionInstaller ?? new FinalVersionInstaller();
        this.tempRootDirectory = string.IsNullOrWhiteSpace(tempRootDirectory) ? Path.GetTempPath() : tempRootDirectory;
    }

    public LoaderKind Kind => LoaderKind.Forge;

    public string DisplayName => "Forge";

    public bool IsImplemented => true;

    public async Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(minecraftVersion, cancellationToken);
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
        string? javaPath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherProgress("Install", $"Installing Forge {minecraftVersion}"));
        var selectedLoaderVersion = loaderVersion;
        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
        {
            var availableVersions = await GetLoaderVersionsAsync(minecraftVersion, cancellationToken);
            selectedLoaderVersion = availableVersions.FirstOrDefault()?.Version;
        }

        if (string.IsNullOrWhiteSpace(selectedLoaderVersion))
            throw new InvalidOperationException($"No Forge loader version available for {minecraftVersion}.");

        var catalog = await GetCatalogAsync(minecraftVersion, cancellationToken);
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

            progress?.Report(new LauncherProgress("Install", $"Downloading Forge {selectedLoaderVersion} installer"));
            await DownloadInstallerAsync(catalogEntry.InstallerUrl, installerJarPath, cancellationToken);

            progress?.Report(new LauncherProgress("Install", $"Running Forge {selectedLoaderVersion} installer"));
            await installerRunner.RunInstallerAsync(javaPath ?? "java", installerJarPath, installerMinecraftDirectory, cancellationToken);

            var sourceVersionName = FindInstalledSourceVersionName(
                installerMinecraftDirectory,
                minecraftVersion,
                selectedLoaderVersion,
                []);

            progress?.Report(new LauncherProgress("Install", $"Finalizing Forge {selectedLoaderVersion} version"));
            var finalVersionName = await CreateFinalVersionAsync(
                installerMinecraftDirectory,
                sourceVersionName,
                isolatedVersionName,
                minecraftVersion,
                cancellationToken);

            await EnsureFinalVersionIsSelfContainedAsync(installerMinecraftDirectory, finalVersionName, cancellationToken);
            CopyFinalVersionDirectory(installerMinecraftDirectory, gameDirectory, finalVersionName);
            CopySharedLibraries(installerMinecraftDirectory, gameDirectory);

            progress?.Report(new LauncherProgress("Install", $"Completing Forge {selectedLoaderVersion} files"));
            await finalVersionInstaller.InstallAsync(gameDirectory, finalVersionName, progress, cancellationToken);

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

    private async Task<IReadOnlyDictionary<string, ForgeCatalogEntry>> GetCatalogAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        await catalogLock.WaitAsync(cancellationToken);
        try
        {
            if (catalogCache.TryGetValue(minecraftVersion, out var cached))
                return cached;

            var supportedVersions = await GetSupportedMinecraftVersionsAsync(cancellationToken);
            if (supportedVersions is not null && !supportedVersions.Contains(minecraftVersion))
            {
                catalogCache[minecraftVersion] = EmptyCatalog();
                return catalogCache[minecraftVersion];
            }

            using var response = await httpClient.GetAsync(GetForgeIndexUrl(minecraftVersion), cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                catalogCache[minecraftVersion] = EmptyCatalog();
                return catalogCache[minecraftVersion];
            }

            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            catalogCache[minecraftVersion] = ParseCatalogEntries(minecraftVersion, html);
            return catalogCache[minecraftVersion];
        }
        finally
        {
            catalogLock.Release();
        }
    }

    private async Task<HashSet<string>?> GetSupportedMinecraftVersionsAsync(CancellationToken cancellationToken)
    {
        if (supportedMinecraftVersions is not null)
            return supportedMinecraftVersions;

        try
        {
            using var response = await httpClient.GetAsync(ForgePromotionsUrl, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                supportedMinecraftVersions = [];
                return supportedMinecraftVersions;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("promos", out var promos)
                || promos.ValueKind is not JsonValueKind.Object)
            {
                supportedMinecraftVersions = [];
                return supportedMinecraftVersions;
            }

            supportedMinecraftVersions = promos.EnumerateObject()
                .Select(property => ExtractMinecraftVersion(property.Name))
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return supportedMinecraftVersions;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractMinecraftVersion(string key)
    {
        var separatorIndex = key.LastIndexOf('-');
        return separatorIndex <= 0 ? string.Empty : key[..separatorIndex];
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

    private async Task DownloadInstallerAsync(Uri installerUrl, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var versionDirectory = Path.Combine(gameDirectory, "versions", finalVersionName);
        var repairService = new ManagedVersionRepairService(httpClient);
        await repairService.EnsureVersionIsSelfContainedAsync(
            gameDirectory,
            finalVersionName,
            versionDirectory,
            cancellationToken);
    }

    private static void CopyFinalVersionDirectory(
        string sourceGameDirectory,
        string destinationGameDirectory,
        string versionName)
    {
        var sourceDirectory = Path.Combine(sourceGameDirectory, "versions", versionName);
        var destinationDirectory = Path.Combine(destinationGameDirectory, "versions", versionName);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Forge final version directory is missing: {sourceDirectory}");

        if (Directory.Exists(destinationDirectory))
            throw new IOException($"Version directory already exists: {versionName}");

        Directory.CreateDirectory(destinationDirectory);
        try
        {
            foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(filePath, destinationPath, overwrite: false);
            }
        }
        catch
        {
            TryDeleteDirectory(destinationDirectory);
            throw;
        }
    }

    private static void CopySharedLibraries(string sourceGameDirectory, string destinationGameDirectory)
    {
        var sourceLibrariesDirectory = Path.Combine(sourceGameDirectory, "libraries");
        if (!Directory.Exists(sourceLibrariesDirectory))
            return;

        var destinationLibrariesDirectory = Path.Combine(destinationGameDirectory, "libraries");
        foreach (var sourceFilePath in Directory.GetFiles(sourceLibrariesDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceLibrariesDirectory, sourceFilePath);
            var destinationPath = Path.Combine(destinationLibrariesDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (!File.Exists(destinationPath))
                File.Copy(sourceFilePath, destinationPath, overwrite: false);
        }
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

    private sealed record VersionJsonMetadata(
        string Id,
        string InheritsFrom,
        string Jar,
        IReadOnlyList<string> LibraryNames);
}
