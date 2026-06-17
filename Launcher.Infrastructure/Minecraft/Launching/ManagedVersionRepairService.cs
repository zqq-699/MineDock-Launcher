using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal interface IManagedVersionRepairService
{
    Task RepairAsync(
        string minecraftDirectory,
        string versionName,
        string instanceDirectory,
        string? javaPath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken);
}

internal sealed class ManagedVersionRepairService : IManagedVersionRepairService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly HttpClient httpClient;

    public ManagedVersionRepairService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
    }

    public async Task RepairAsync(
        string minecraftDirectory,
        string versionName,
        string instanceDirectory,
        string? javaPath,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var versionDirectory = ResolveVersionDirectory(minecraftDirectory, versionName, instanceDirectory);
        if (!Directory.Exists(versionDirectory))
            throw new InstanceRepairException($"Version directory is missing for {versionName}.");

        ReportProgress(progress, LaunchProgressStages.CheckingInstance, "Checking instance files", 6);

        ReportProgress(progress, LaunchProgressStages.RepairingMetadata, "Repairing version metadata", 18);
        var resolvedVersion = await EnsureVersionIsSelfContainedAsync(
            minecraftDirectory,
            versionName,
            versionDirectory,
            cancellationToken);

        ReportProgress(progress, LaunchProgressStages.RepairingJar, "Repairing instance jar", 32);
        await EnsureVersionJarAsync(versionDirectory, versionName, resolvedVersion, cancellationToken);

        ReportProgress(progress, LaunchProgressStages.RepairingLibraries, "Repairing shared libraries", 48);
        await EnsureLibrariesAsync(minecraftDirectory, resolvedVersion.VersionJson, cancellationToken);

        ReportProgress(progress, LaunchProgressStages.RepairingAssets, "Repairing shared assets", 64);
        await EnsureAssetsAsync(minecraftDirectory, resolvedVersion.VersionJson, cancellationToken);

        ReportProgress(progress, LaunchProgressStages.RepairingLogging, "Repairing logging configuration", 80);
        await EnsureLoggingAsync(minecraftDirectory, resolvedVersion.VersionJson, cancellationToken);

        ReportProgress(progress, LaunchProgressStages.CheckingJava, "Checking Java runtime", 90);
        EnsureJavaIsUsable(javaPath);
    }

    internal async Task<ResolvedVersionMetadata> EnsureVersionIsSelfContainedAsync(
        string minecraftDirectory,
        string versionName,
        string versionDirectory,
        CancellationToken cancellationToken)
    {
        var result = await ResolveCurrentVersionAsync(
            minecraftDirectory,
            versionName,
            versionDirectory,
            cancellationToken);

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
        CancellationToken cancellationToken)
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
                WasModified: false);
        }

        var parent = await ResolveParentVersionAsync(minecraftDirectory, inheritsFrom, cancellationToken);
        var mergedVersion = VersionJsonMergeHelper.MergeFlattenedVersion(parent.VersionJson, versionJson, versionName);

        return new ResolvedVersionMetadata(
            versionName,
            mergedVersion,
            parent.LocalJarPath,
            VanillaVersionMetadataClient.GetClientJarUrl(mergedVersion) ?? parent.ClientJarUrl ?? currentJarUrl,
            WasModified: true);
    }

    private async Task<ResolvedVersionMetadata> ResolveParentVersionAsync(
        string minecraftDirectory,
        string parentVersionName,
        CancellationToken cancellationToken)
    {
        var parentDirectory = Path.Combine(minecraftDirectory, "versions", parentVersionName);
        if (Directory.Exists(parentDirectory)
            && File.Exists(Path.Combine(parentDirectory, $"{parentVersionName}.json")))
        {
            return await ResolveCurrentVersionAsync(
                minecraftDirectory,
                parentVersionName,
                parentDirectory,
                cancellationToken);
        }

        JsonObject remoteVersionJson;
        try
        {
            remoteVersionJson = await VanillaVersionMetadataClient.DownloadVersionJsonAsync(
                httpClient,
                parentVersionName,
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
            WasModified: false);
    }

    private static JsonObject NormalizeVersionJson(JsonObject versionJson, string versionName)
    {
        var normalized = (JsonObject)versionJson.DeepClone();
        normalized["id"] = versionName;
        normalized["jar"] = versionName;
        normalized.Remove("inheritsFrom");
        return normalized;
    }

    private async Task EnsureVersionJarAsync(
        string versionDirectory,
        string versionName,
        ResolvedVersionMetadata resolvedVersion,
        CancellationToken cancellationToken)
    {
        var jarPath = Path.Combine(versionDirectory, $"{versionName}.jar");
        if (File.Exists(jarPath))
            return;

        if (!string.IsNullOrWhiteSpace(resolvedVersion.LocalJarPath)
            && File.Exists(resolvedVersion.LocalJarPath))
        {
            File.Copy(resolvedVersion.LocalJarPath, jarPath, overwrite: false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(resolvedVersion.ClientJarUrl))
        {
            await DownloadFileAsync(resolvedVersion.ClientJarUrl, jarPath, cancellationToken);
            return;
        }

        throw new InstanceRepairException($"Version {versionName} is missing its client jar and no repair source is available.");
    }

    private async Task EnsureLibrariesAsync(
        string minecraftDirectory,
        JsonObject versionJson,
        CancellationToken cancellationToken)
    {
        if (versionJson["libraries"] is not JsonArray libraries)
            return;

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

                await DownloadFileAsync(artifact.Url, destinationPath, cancellationToken);
            }
        }
    }

    private async Task EnsureAssetsAsync(
        string minecraftDirectory,
        JsonObject versionJson,
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
            await DownloadFileAsync(assetIndexUrl, indexPath, cancellationToken);

        await using var indexStream = File.OpenRead(indexPath);
        var indexNode = await JsonNode.ParseAsync(indexStream, cancellationToken: cancellationToken)
            ?? throw new InstanceRepairException($"Asset index {assetIndexId} is empty.");
        if (indexNode["objects"] is not JsonObject objects)
            return;

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

            var assetUrl = $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";
            await DownloadFileAsync(assetUrl, objectPath, cancellationToken);
        }
    }

    private async Task EnsureLoggingAsync(
        string minecraftDirectory,
        JsonObject versionJson,
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

        await DownloadFileAsync(url, logConfigPath, cancellationToken);
    }

    private static void EnsureJavaIsUsable(string? javaPath)
    {
        if (!string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath))
            return;

        var command = OperatingSystem.IsWindows() ? "where" : "which";
        var argument = OperatingSystem.IsWindows() ? "java" : "java";
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = argument,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();
            if (process.ExitCode == 0)
                return;
        }
        catch
        {
        }

        throw new InstanceRepairException("No usable Java runtime was found.");
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

        return new LibraryArtifact(url, relativePath);
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

        return new LibraryArtifact(new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath).AbsoluteUri, relativePath);
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

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
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
        bool WasModified);

    private sealed record LibraryArtifact(string Url, string RelativePath);
}
