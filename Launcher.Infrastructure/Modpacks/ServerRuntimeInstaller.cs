/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Modpacks;

internal sealed class ServerRuntimeInstaller : IServerRuntimeInstaller
{
    private const string FabricLauncherJar = "fabric-server-launch.jar";
    private const string QuiltLauncherJar = "quilt-server-launch.jar";
    private readonly HttpClient httpClient;
    private readonly ISettingsService settingsService;
    private readonly IDownloadSpeedLimitState? downloadSpeedLimitState;
    private readonly LoaderInstallerJavaRuntimeResolver? javaRuntimeResolver;
    private readonly IForgeInstallerRunner forgeInstallerRunner;
    private readonly ILogger<ServerRuntimeInstaller> logger;

    public ServerRuntimeInstaller(
        ISettingsService settingsService,
        LoaderInstallerJavaRuntimeResolver javaRuntimeResolver,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<ServerRuntimeInstaller>? logger = null)
        : this(
            MinecraftHttpClientFactory.CreateTransportClient(),
            settingsService,
            javaRuntimeResolver,
            new ForgeInstallerRunner(),
            downloadSpeedLimitState,
            logger)
    {
    }

    internal ServerRuntimeInstaller(
        HttpClient httpClient,
        ISettingsService settingsService,
        LoaderInstallerJavaRuntimeResolver? javaRuntimeResolver = null,
        IForgeInstallerRunner? forgeInstallerRunner = null,
        IDownloadSpeedLimitState? downloadSpeedLimitState = null,
        ILogger<ServerRuntimeInstaller>? logger = null)
    {
        this.httpClient = httpClient;
        this.settingsService = settingsService;
        this.javaRuntimeResolver = javaRuntimeResolver;
        this.forgeInstallerRunner = forgeInstallerRunner ?? new ForgeInstallerRunner();
        this.downloadSpeedLimitState = downloadSpeedLimitState;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ServerRuntimeInstaller>.Instance;
    }

    public async Task InstallAsync(
        PreparedModpack modpack,
        string targetDirectory,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modpack);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        if (string.IsNullOrWhiteSpace(modpack.MinecraftVersion))
            throw new ModpackImportException(ModpackImportFailureReason.InvalidManifest, "Minecraft version is missing.");

        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var preference = settings.DownloadSourcePreference;
        var speedLimit = settings.DownloadSpeedLimitMbPerSecond;
        Directory.CreateDirectory(targetDirectory);
        MinecraftPathGuard.EnsureNoReparsePoints(targetDirectory, targetDirectory, "Server runtime directory");

        progress?.Report(new LauncherProgress(ImportProgressStages.InstallingMinecraftBase, string.Empty));
        var versionJson = await VanillaVersionMetadataClient.DownloadVersionJsonAsync(
            httpClient,
            modpack.MinecraftVersion,
            preference,
            speedLimit,
            downloadSpeedLimitState,
            logger,
            cancellationToken).ConfigureAwait(false);
        var serverJarName = $"minecraft_server.{modpack.MinecraftVersion}.jar";
        await DownloadServerJarAsync(
            versionJson,
            targetDirectory,
            serverJarName,
            preference,
            speedLimit,
            progress,
            cancellationToken).ConfigureAwait(false);

        var launchJar = serverJarName;
        switch (modpack.Loader)
        {
            case LoaderKind.Vanilla:
                break;
            case LoaderKind.Fabric:
                launchJar = await InstallProfileLoaderAsync(
                    modpack,
                    targetDirectory,
                    serverJarName,
                    isQuilt: false,
                    preference,
                    speedLimit,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                break;
            case LoaderKind.Quilt:
                launchJar = await InstallProfileLoaderAsync(
                    modpack,
                    targetDirectory,
                    serverJarName,
                    isQuilt: true,
                    preference,
                    speedLimit,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                break;
            case LoaderKind.Forge:
            case LoaderKind.NeoForge:
                await InstallForgeLikeAsync(
                    modpack,
                    targetDirectory,
                    preference,
                    speedLimit,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                launchJar = ResolveForgeLikeLaunchJar(targetDirectory, serverJarName);
                break;
            default:
                throw new ModpackImportException(
                    ModpackImportFailureReason.UnsupportedLoader,
                    $"Unsupported server loader: {modpack.Loader}");
        }

        WriteLaunchScriptsIfMissing(targetDirectory, launchJar);
    }

    private async Task DownloadServerJarAsync(
        JsonObject versionJson,
        string targetDirectory,
        string serverJarName,
        DownloadSourcePreference preference,
        int speedLimit,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var url = VanillaVersionMetadataClient.GetServerJarUrl(versionJson);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                "The selected Minecraft version has no dedicated server download.");
        }
        await DownloadArtifactAsync(
            url,
            Path.Combine(targetDirectory, serverJarName),
            VanillaVersionMetadataClient.GetServerJarSha1(versionJson),
            VanillaVersionMetadataClient.GetServerJarSize(versionJson),
            targetDirectory,
            "Mojang",
            preference,
            speedLimit,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> InstallProfileLoaderAsync(
        PreparedModpack modpack,
        string targetDirectory,
        string serverJarName,
        bool isQuilt,
        DownloadSourcePreference preference,
        int speedLimit,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modpack.LoaderVersion))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"{modpack.Loader} loader version is missing.");
        }
        progress?.Report(new LauncherProgress(ImportProgressStages.InstallingLoader, string.Empty));
        var profileUrl = isQuilt
            ? $"https://meta.quiltmc.org/v3/versions/loader/{modpack.MinecraftVersion}/{modpack.LoaderVersion}/server/json"
            : $"https://meta.fabricmc.net/v2/versions/loader/{modpack.MinecraftVersion}/{modpack.LoaderVersion}/server/json";
        var executor = CreateExecutor(speedLimit, DownloadConcurrencyCategory.Metadata);
        var profile = await executor.ExecuteAsync(
            profileUrl,
            preference,
            isQuilt ? "Quilt" : "Fabric",
            async (context, token) =>
            {
                await using var source = await context.Response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var node = await JsonNode.ParseAsync(source, cancellationToken: token).ConfigureAwait(false);
                return node as JsonObject
                    ?? throw new DownloadContentValidationException("Server loader profile is not a JSON object.");
            },
            cancellationToken).ConfigureAwait(false);

        var artifacts = ResolveProfileLibraries(profile).ToArray();
        var librariesRoot = Path.Combine(targetDirectory, "libraries");
        await Parallel.ForEachAsync(
            artifacts,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 8 },
            async (artifact, token) =>
            {
                var destination = MinecraftPathGuard.EnsureWithin(
                    Path.Combine(librariesRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    librariesRoot,
                    "Server loader library");
                await DownloadArtifactAsync(
                    artifact.Url,
                    destination,
                    artifact.Sha1,
                    artifact.Size,
                    librariesRoot,
                    artifact.ResourceCategory,
                    preference,
                    speedLimit,
                    progress,
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var launchJarName = isQuilt ? QuiltLauncherJar : FabricLauncherJar;
        var profileMainClass = TryGetString(profile, "mainClass");
        CreateProfileLauncherJar(
            Path.Combine(targetDirectory, launchJarName),
            isQuilt
                ? TryGetString(profile, "launcherMainClass")
                    ?? "org.quiltmc.loader.impl.launch.server.QuiltServerLauncher"
                : "net.fabricmc.loader.launch.server.FabricServerLauncher",
            artifacts.Select(artifact => $"libraries/{artifact.RelativePath.Replace('\\', '/')}").ToArray(),
            isQuilt ? null : profileMainClass);
        var propertiesName = isQuilt
            ? "quilt-server-launcher.properties"
            : "fabric-server-launcher.properties";
        var properties = isQuilt
            ? $"serverJar={serverJarName}{Environment.NewLine}"
            : $"serverJar={serverJarName}{Environment.NewLine}launch.mainClass={profileMainClass}{Environment.NewLine}";
        await File.WriteAllTextAsync(
            Path.Combine(targetDirectory, propertiesName),
            properties,
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        return launchJarName;
    }

    private static IEnumerable<ManagedLibraryArtifact> ResolveProfileLibraries(JsonObject profile)
    {
        if (profile["libraries"] is not JsonArray libraries)
            yield break;
        foreach (var libraryNode in libraries)
        {
            if (libraryNode is not JsonObject library || !ManagedLibraryArtifactResolver.IsAllowed(library))
                continue;
            foreach (var artifact in ManagedLibraryArtifactResolver.EnumerateDownloads(library))
                yield return artifact;
        }
    }

    private static string TryGetString(JsonObject source, string propertyName) =>
        source[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : string.Empty;

    private async Task InstallForgeLikeAsync(
        PreparedModpack modpack,
        string targetDirectory,
        DownloadSourcePreference preference,
        int speedLimit,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modpack.LoaderVersion))
        {
            throw new ModpackImportException(
                ModpackImportFailureReason.InvalidManifest,
                $"{modpack.Loader} loader version is missing.");
        }
        progress?.Report(new LauncherProgress(ImportProgressStages.InstallingLoader, string.Empty));
        var coordinate = modpack.Loader is LoaderKind.Forge
            ? $"{modpack.MinecraftVersion}-{modpack.LoaderVersion}"
            : modpack.LoaderVersion;
        var installerUrl = modpack.Loader is LoaderKind.Forge
            ? $"https://maven.minecraftforge.net/net/minecraftforge/forge/{coordinate}/forge-{coordinate}-installer.jar"
            : $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{coordinate}/neoforge-{coordinate}-installer.jar";
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"blockhelm-server-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var installerPath = Path.Combine(tempDirectory, "installer.jar");
            var sha1 = await TryDownloadTextAsync(installerUrl + ".sha1", preference, speedLimit, cancellationToken)
                .ConfigureAwait(false);
            await DownloadArtifactAsync(
                installerUrl,
                installerPath,
                NormalizeSha1(sha1),
                expectedSize: null,
                tempDirectory,
                modpack.Loader is LoaderKind.Forge ? "Forge" : "NeoForge",
                preference,
                speedLimit,
                progress,
                cancellationToken).ConfigureAwait(false);
            progress?.Report(new LauncherProgress(InstallProgressStages.CheckingJava, string.Empty));
            var resolver = javaRuntimeResolver
                ?? throw new InvalidOperationException("The loader installer Java runtime resolver is not configured.");
            var runtime = await resolver.ResolveAsync(
                new LoaderInstallerJavaRuntimeRequest(
                    modpack.MinecraftVersion,
                    Path.GetFileName(targetDirectory),
                    modpack.Loader,
                    modpack.LoaderVersion,
                    targetDirectory,
                    preference,
                    speedLimit,
                    progress),
                cancellationToken).ConfigureAwait(false);
            progress?.Report(new LauncherProgress(InstallProgressStages.RunningLoaderInstaller, string.Empty));
            await forgeInstallerRunner.RunServerInstallerAsync(
                runtime.ExecutablePath,
                installerPath,
                targetDirectory,
                cancellationToken).ConfigureAwait(false);
            ValidateForgeLikeInstall(targetDirectory);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task DownloadArtifactAsync(
        string url,
        string destination,
        string? expectedSha1,
        long? expectedSize,
        string managedRoot,
        string category,
        DownloadSourcePreference preference,
        int speedLimit,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
        var executor = CreateExecutor(speedLimit, DownloadConcurrencyCategory.Runtime);
        await executor.DownloadFileAsync(
            url,
            preference,
            category,
            destination,
            expectedSha1,
            expectedSize,
            cancellationToken,
            reportAttemptProgress: (_, transferred, total) => progress?.Report(new LauncherProgress(
                ModProgressStages.DownloadingFile,
                Path.GetFileName(destination),
                total is > 0 ? transferred * 100d / total.Value : null)),
            options: new DownloadFileOptions(ManagedRoot: managedRoot),
            speedMeter: SpeedMeterProgress.TryGet(progress)).ConfigureAwait(false);
    }

    private MinecraftDownloadRequestExecutor CreateExecutor(int speedLimit, DownloadConcurrencyCategory category) =>
        new(
            httpClient,
            logger,
            DownloadBandwidthLimiter.Create(speedLimit, downloadSpeedLimitState),
            category: category);

    private async Task<string?> TryDownloadTextAsync(
        string url,
        DownloadSourcePreference preference,
        int speedLimit,
        CancellationToken cancellationToken)
    {
        try
        {
            var executor = CreateExecutor(speedLimit, DownloadConcurrencyCategory.Metadata);
            return await executor.ExecuteAsync(
                url,
                preference,
                "ThirdParty",
                async (context, token) => await context.Response.Content.ReadAsStringAsync(token).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Loader installer SHA1 metadata was unavailable.");
            return null;
        }
    }

    private static string? NormalizeSha1(string? value)
    {
        var candidate = value?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return candidate is { Length: 40 } && candidate.All(Uri.IsHexDigit) ? candidate : null;
    }

    private static void ValidateForgeLikeInstall(string targetDirectory)
    {
        var hasRunScript = File.Exists(Path.Combine(targetDirectory, "run.bat"))
            || File.Exists(Path.Combine(targetDirectory, "run.sh"));
        var hasLauncherJar = Directory.EnumerateFiles(targetDirectory, "*.jar", SearchOption.TopDirectoryOnly)
            .Any(path => Path.GetFileName(path).Contains("forge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), "server.jar", StringComparison.OrdinalIgnoreCase));
        if (!hasRunScript && !hasLauncherJar)
            throw new InvalidDataException("The server loader installer did not produce a runnable server.");
    }

    private static string ResolveForgeLikeLaunchJar(string targetDirectory, string fallback)
    {
        var serverJar = Path.Combine(targetDirectory, "server.jar");
        if (File.Exists(serverJar))
            return "server.jar";
        return Directory.EnumerateFiles(targetDirectory, "*.jar", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .FirstOrDefault(name => name?.Contains("forge", StringComparison.OrdinalIgnoreCase) == true)
            ?? fallback;
    }

    private static void CreateProfileLauncherJar(
        string path,
        string launcherMainClass,
        IReadOnlyList<string> classPath,
        string? launchMainClass)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("META-INF/MANIFEST.MF", CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false), leaveOpen: false))
        {
            writer.NewLine = "\r\n";
            writer.WriteLine("Manifest-Version: 1.0");
            WriteManifestAttribute(writer, "Main-Class", launcherMainClass);
            if (classPath.Count > 0)
                WriteManifestAttribute(writer, "Class-Path", string.Join(' ', classPath));
            writer.WriteLine();
        }
        if (!string.IsNullOrWhiteSpace(launchMainClass))
        {
            var propertiesEntry = archive.CreateEntry("fabric-server-launch.properties", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(propertiesEntry.Open(), new UTF8Encoding(false));
            writer.WriteLine($"launch.mainClass={launchMainClass}");
        }
    }

    private static void WriteManifestAttribute(TextWriter writer, string name, string value)
    {
        var line = $"{name}: {value}";
        const int maxLength = 70;
        writer.WriteLine(line[..Math.Min(maxLength, line.Length)]);
        for (var index = maxLength; index < line.Length; index += maxLength - 1)
            writer.WriteLine(" " + line.Substring(index, Math.Min(maxLength - 1, line.Length - index)));
    }

    private static void WriteLaunchScriptsIfMissing(string targetDirectory, string launchJar)
    {
        var knownScripts = new[] { "run.bat", "run.sh", "LaunchServer.bat", "LaunchServer.sh", "start.bat", "start.sh" };
        if (knownScripts.Any(name => File.Exists(Path.Combine(targetDirectory, name))))
            return;
        File.WriteAllText(
            Path.Combine(targetDirectory, "LaunchServer.bat"),
            $"@echo off{Environment.NewLine}java -Xmx2G -jar \"{launchJar}\" nogui{Environment.NewLine}pause{Environment.NewLine}",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(targetDirectory, "LaunchServer.sh"),
            $"#!/bin/sh\ncd \"$(dirname \"$0\")\"\njava -Xmx2G -jar \"{launchJar}\" nogui\n",
            new UTF8Encoding(false));
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
}
