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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ForgeLoaderProviderTests : TestTempDirectory
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ForgeLoaderProviderPassesSelectedJavaPathToInstallerRunner()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var expectedJavaPath = Path.Combine(TempRoot, "Selected Java", "bin", "java.exe");
        string? receivedJavaPath = null;
        var javaRuntimeResolver = new FixedJavaRuntimeResolver(expectedJavaPath);
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((gameDirectory, javaPath, _) =>
            {
                receivedJavaPath = javaPath;
                return CreateSandboxForgeInstallAsync(
                    gameDirectory,
                    "forge-1.20.1-47.4.20",
                    "1.20.1",
                    "1.20.1-47.4.20");
            }),
            javaRuntimeResolver: javaRuntimeResolver);

        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null);

        Assert.Equal(expectedJavaPath, receivedJavaPath);
        Assert.Equal(minecraftDirectory, javaRuntimeResolver.LastRequest?.MinecraftDirectory);
        Assert.Equal(DownloadSourcePreference.Official, javaRuntimeResolver.LastRequest?.DownloadSourcePreference);
        Assert.Equal(LoaderKind.Forge, javaRuntimeResolver.LastRequest?.Loader);
        Assert.Equal("47.4.20", javaRuntimeResolver.LastRequest?.LoaderVersion);
    }

    [Fact]
    public async Task ForgeLoaderProviderDoesNotStartInstallerWhenJavaSelectionFails()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var runnerStarted = false;
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((_, _, _) =>
            {
                runnerStarted = true;
                return Task.CompletedTask;
            }),
            javaRuntimeResolver: new FailingJavaRuntimeResolver());

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing, exception.Reason);
        Assert.False(runnerStarted);
    }

    [Fact]
    public async Task ForgeInstallerRunnerDoesNotStartWhenAlreadyCanceled()
    {
        var started = false;
        var runner = new ForgeInstallerRunner(_ =>
        {
            started = true;
            return null;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunInstallerAsync(
            Path.Combine(TempRoot, "ignored-java.exe"),
            Path.Combine(TempRoot, "ignored-installer.jar"),
            TempRoot,
            cancellation.Token));

        Assert.False(started);
    }

    [Fact]
    public async Task ForgeInstallerRunnerRejectsPathJavaFallbackWithoutStartingProcess()
    {
        var started = false;
        var runner = new ForgeInstallerRunner(_ =>
        {
            started = true;
            return null;
        });

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunInstallerAsync(
            "java",
            Path.Combine(TempRoot, "installer.jar"),
            TempRoot,
            CancellationToken.None));

        Assert.False(started);
    }

    [Fact]
    public async Task ForgeIntegrityRepairUsesInstallerManifestWithoutLegacyMarkerAndPreservesUserContent()
    {
        const string versionName = "Better MC [FORGE] BMC4";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var httpClient = new HttpClient(new ForgeHttpHandler());
        var provider = new ForgeLoaderProvider(
            httpClient,
            new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
                CreateSandboxForgeInstallAsync(
                    gameDirectory,
                    "forge-1.20.1-47.4.20",
                    "1.20.1",
                    "1.20.1-47.4.20")),
            new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: new FixedJavaRuntimeResolver());
        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            versionName,
            "47.4.20",
            progress: null);
        await EnsureUnverifiedVersionLibrariesExistAsync(minecraftDirectory, versionName);
        var versionJsonPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var versionJson = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();
        versionJson["launcher"]!.AsObject()["forgeProcessorArtifacts"] = new JsonObject
        {
            ["schemaVersion"] = 2
        };
        await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString());

        var missingRelativePaths = new[]
        {
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar",
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-extra.jar",
            "net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-client.jar"
        };
        foreach (var relativePath in missingRelativePaths)
            File.Delete(GetGeneratedLibraryPath(minecraftDirectory, relativePath));

        var userFiles = new Dictionary<string, string>
        {
            [Path.Combine(minecraftDirectory, "mods", "keep.jar")] = "mod",
            [Path.Combine(minecraftDirectory, "config", "keep.toml")] = "config",
            [Path.Combine(minecraftDirectory, "saves", "World", "level.dat")] = "save"
        };
        foreach (var userFile in userFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userFile.Key)!);
            await File.WriteAllTextAsync(userFile.Key, userFile.Value);
        }

        var service = new GameFileIntegrityService(
            httpClient,
            downloadSpeedLimitState: null,
            logger: null,
            loaderProviders: [provider],
            gameInstallCoordinator: new GameInstallCoordinator());
        var progressReports = new ConcurrentQueue<LauncherProgress>();
        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(
                minecraftDirectory,
                versionName,
                Path.Combine(minecraftDirectory, "versions", versionName))
            {
                LoaderIdentity = new GameFileLoaderIdentity(
                    LoaderKind.Forge,
                    "1.20.1",
                    "47.4.20")
            },
            new GameFileRepairOptions(AllowRepair: true),
            new InlineProgress(progressReports));

        Assert.True(
            result.LaunchAllowed,
            string.Join(Environment.NewLine, result.Failures.Select(failure =>
                $"{failure.Category}: {failure.Reason} {failure.TargetPath} {failure.Source}")));
        Assert.True(result.RepairedCount >= missingRelativePaths.Length);
        Assert.All(missingRelativePaths, relativePath =>
            Assert.True(File.Exists(GetGeneratedLibraryPath(minecraftDirectory, relativePath))));
        foreach (var userFile in userFiles)
            Assert.Equal(userFile.Value, await File.ReadAllTextAsync(userFile.Key));

        var manifest = await LoaderArtifactManifestStore.ReadAsync(
            Path.Combine(minecraftDirectory, "versions", versionName),
            new GameFileLoaderIdentity(LoaderKind.Forge, "1.20.1", "47.4.20"),
            CancellationToken.None);
        Assert.True(manifest.IsValid);
        Assert.All(missingRelativePaths, relativePath =>
            Assert.Contains(
                manifest.Manifest!.Artifacts,
                artifact => artifact.RelativePath == $"libraries/{relativePath}"));
        using var repairedVersionJson = JsonDocument.Parse(await File.ReadAllTextAsync(versionJsonPath));
        Assert.False(
            repairedVersionJson.RootElement.GetProperty("launcher").TryGetProperty(
                "forgeProcessorArtifacts",
                out _));

        var visibleProgress = progressReports
            .Where(report => report.DownloadSpeedTelemetry is null)
            .ToArray();
        Assert.DoesNotContain(
            visibleProgress,
            report => report.Stage.StartsWith("Install.", StringComparison.Ordinal));
        var expectedStages = new[]
        {
            LaunchProgressStages.RepairingLoaderInstaller,
            LaunchProgressStages.CheckingJava,
            LaunchProgressStages.RunningLoaderInstaller,
            LaunchProgressStages.FinalizingLoaderVersion,
            LaunchProgressStages.PublishingLoaderArtifacts,
            LaunchProgressStages.RevalidatingFiles
        };
        var previousIndex = -1;
        foreach (var stage in expectedStages)
        {
            var index = Array.FindIndex(visibleProgress, report => report.Stage == stage);
            Assert.True(index > previousIndex, $"Launch progress stage {stage} was missing or out of order.");
            previousIndex = index;
        }
        var percents = visibleProgress
            .Where(report => report.Percent is not null)
            .Select(report => report.Percent!.Value)
            .ToArray();
        Assert.Equal(percents.Order(), percents);
        Assert.Equal(90, visibleProgress.Last(report => report.Stage == LaunchProgressStages.RevalidatingFiles).Percent);
    }

    private static int? TryReadProcessId(string path)
    {
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var processId)
                ? processId
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException(timeoutMessage);

            await Task.Delay(25);
        }
    }

    private ForgeLoaderProvider CreateProvider(
        IForgeInstallerRunner? runner = null,
        IFinalVersionInstaller? finalVersionInstaller = null,
        ILoaderInstallerJavaRuntimeResolver? javaRuntimeResolver = null)
    {
        return new ForgeLoaderProvider(
            new HttpClient(new ForgeHttpHandler()),
            runner ?? new NoOpForgeInstallerRunner(),
            finalVersionInstaller ?? new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: javaRuntimeResolver ?? new FixedJavaRuntimeResolver());
    }

    private static async Task CreateSandboxForgeInstallAsync(
        string minecraftDirectory,
        string versionName,
        string inheritsFrom,
        string combinedForgeVersion)
    {
        await CreateVanillaVersionAsync(minecraftDirectory, inheritsFrom);
        CreateForgeDerivedVersion(minecraftDirectory, versionName, inheritsFrom, combinedForgeVersion);
        CreateGeneratedForgeLibrary(minecraftDirectory, combinedForgeVersion);
    }

    private static async Task CreateVanillaVersionAsync(string minecraftDirectory, string versionName)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            $$"""
            {
              "id": "{{versionName}}",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": [
                { "name": "com.mojang:patchy:2.2.10" }
              ],
              "arguments": {
                "game": [ "--username", "${auth_player_name}" ],
                "jvm": [ "-Djava.library.path=${natives_directory}" ]
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, $"{versionName}.jar"), "base jar");
    }

    private static void CreateForgeDerivedVersion(string minecraftDirectory, string versionName, string inheritsFrom, string combinedForgeVersion)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var versionJson = new JsonObject
        {
            ["id"] = versionName,
            ["inheritsFrom"] = inheritsFrom,
            ["mainClass"] = "net.minecraftforge.client.loading.ClientModLoader",
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = $"net.minecraftforge:forge:{combinedForgeVersion}" }
            }
        };

        File.WriteAllText(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(versionDirectory, "win_args.txt"),
            "--launchTarget forge_client");
    }

    private static void CreateGeneratedForgeLibrary(string minecraftDirectory, string combinedForgeVersion)
    {
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-extra.jar",
            "minecraft extra");
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraft/client/1.20.1-20230612.114412/client-1.20.1-20230612.114412-srg.jar",
            "minecraft srg");
        WriteGeneratedLibrary(
            minecraftDirectory,
            $"net/minecraftforge/forge/{combinedForgeVersion}/forge-{combinedForgeVersion}-client.jar",
            "patched forge client");
        WriteGeneratedLibrary(
            minecraftDirectory,
            "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
            "forge runtime");
    }

    private static void WriteGeneratedLibrary(string minecraftDirectory, string relativePath, string content)
    {
        var path = GetGeneratedLibraryPath(minecraftDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class InlineProgress(ConcurrentQueue<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Enqueue(value);
    }

    private static async Task EnsureUnverifiedVersionLibrariesExistAsync(
        string minecraftDirectory,
        string versionName)
    {
        var versionPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var version = JsonNode.Parse(await File.ReadAllTextAsync(versionPath))!.AsObject();
        if (version["libraries"] is not JsonArray libraries)
            return;
        foreach (var library in libraries.OfType<JsonObject>())
        {
            foreach (var artifact in ManagedLibraryArtifactResolver.EnumerateDownloads(library))
            {
                if (MinecraftFileIntegrity.IsSha1(artifact.Sha1))
                    continue;
                var path = GetGeneratedLibraryPath(minecraftDirectory, artifact.RelativePath);
                if (File.Exists(path))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, "standard library");
            }
        }
    }

    private static string GetGeneratedLibraryPath(string minecraftDirectory, string relativePath) =>
        Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] CreateModernForgeInstallerBytes()
    {
        static string Sha1(string content) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(profileEntry.Open()))
            {
                writer.Write(
                    $$"""
                {
                  "spec": 1,
                  "minecraft": "1.20.1",
                  "data": {
                    "MC_EXTRA": { "client": "[net.minecraft:client:1.20.1-20230612.114412:extra]" },
                    "MC_EXTRA_SHA": { "client": "'{{Sha1("minecraft extra")}}'" },
                    "MC_SRG": { "client": "[net.minecraft:client:1.20.1-20230612.114412:srg]" },
                    "PATCHED": { "client": "[net.minecraftforge:forge:1.20.1-47.4.20:client]" },
                    "PATCHED_SHA": { "client": "'{{Sha1("patched forge client")}}'" }
                  },
                  "libraries": [
                    {
                      "name": "net.minecraftforge:fmlcore:1.20.1-47.4.20",
                      "downloads": {
                        "artifact": {
                          "path": "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
                          "sha1": "{{Sha1("forge runtime")}}",
                          "size": 13
                        }
                      }
                    }
                  ],
                  "processors": [
                    { "sides": ["client"], "args": ["--extra", "{MC_EXTRA}"], "outputs": { "{MC_EXTRA}": "{MC_EXTRA_SHA}" } },
                    { "args": ["--output", "{MC_SRG}"] },
                    { "args": ["--output", "{PATCHED}"], "outputs": { "{PATCHED}": "{PATCHED_SHA}" } }
                  ]
                }
                """);
            }
            var runtimeEntry = archive.CreateEntry(
                "maven/net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar");
            using (var runtimeWriter = new StreamWriter(runtimeEntry.Open()))
            {
                runtimeWriter.Write("forge runtime");
            }
            var versionEntry = archive.CreateEntry("version.json");
            using var versionWriter = new StreamWriter(versionEntry.Open());
            versionWriter.Write(
                """
                {
                  "libraries": [
                    {
                      "name": "net.minecraftforge:fmlcore:1.20.1-47.4.20",
                      "downloads": {
                        "artifact": {
                          "path": "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar",
                          "sha1": "9de99c8b24ff448def492a91d4aa09e29511b66c",
                          "size": 13
                        }
                      }
                    }
                  ]
                }
                """);
        }
        return stream.ToArray();
    }

    private sealed class NoOpForgeInstallerRunner : IForgeInstallerRunner
    {
        public Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpFinalVersionInstaller : IFinalVersionInstaller
    {
        public Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedJavaRuntimeResolver(string? executablePath = null) : ILoaderInstallerJavaRuntimeResolver
    {
        public LoaderInstallerJavaRuntimeRequest? LastRequest { get; private set; }

        public Task<JavaRuntimeInfo> ResolveAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var path = executablePath ?? Path.Combine("C:\\Program Files", "Launcher Java", "bin", "java.exe");
            return Task.FromResult(new JavaRuntimeInfo(
                "Launcher Java 21",
                "21.0.2",
                21,
                "x64",
                path,
                Path.GetDirectoryName(Path.GetDirectoryName(path))!,
                "Test"));
        }
    }

    private sealed class FailingJavaRuntimeResolver : ILoaderInstallerJavaRuntimeResolver
    {
        public Task<JavaRuntimeInfo> ResolveAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new JavaRuntimeSelectionException(
                "No compatible Java runtime is available.",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing,
                17);
        }
    }

    private sealed class ScriptedForgeInstallerRunner : IForgeInstallerRunner
    {
        private readonly Func<string, string, string, Task> callback;

        public ScriptedForgeInstallerRunner(Func<string, string, string, Task> callback)
        {
            this.callback = callback;
        }

        public Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
        {
            return callback(minecraftDirectory, javaCommand, installerJarPath);
        }
    }

    private sealed class ForgeHttpHandler : HttpMessageHandler
    {
        private readonly bool include1201Html;
        private readonly bool include1102Html;
        private readonly byte[]? legacyInstallerBytes;
        private readonly string promotionsJson;

        public ForgeHttpHandler(
            bool include1201Html = true,
            bool include1102Html = false,
            string? promotionsJson = null,
            byte[]? legacyInstallerBytes = null)
        {
            this.include1201Html = include1201Html;
            this.include1102Html = include1102Html;
            this.legacyInstallerBytes = legacyInstallerBytes;
            this.promotionsJson = promotionsJson ?? """
                {
                  "promos": {
                    "1.20.1-latest": "47.4.20",
                    "1.20.1-recommended": "47.4.10"
                  }
                }
                """;
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var uri = request.RequestUri!.AbsoluteUri
                .Replace("https://bmclapi2.bangbang93.com/maven/", "https://maven.minecraftforge.net/", StringComparison.OrdinalIgnoreCase);
            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json")
            {
                return Task.FromResult(CreateJsonResponse(request, promotionsJson));
            }

            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.20.1.html")
            {
                if (!include1201Html)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

                return Task.FromResult(CreateHtmlResponse(request, """
                    <html>
                      <body>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar">47.4.20</a>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.10/forge-1.20.1-47.4.10-installer.jar">47.4.10</a>
                      </body>
                    </html>
                    """));
            }

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request));

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.10/forge-1.20.1-47.4.10-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request));

            if (uri == "https://files.minecraftforge.net/net/minecraftforge/forge/index_1.10.2.html")
            {
                if (!include1102Html)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

                return Task.FromResult(CreateHtmlResponse(request, """
                    <html>
                      <body>
                        <a href="https://maven.minecraftforge.net/net/minecraftforge/forge/1.10.2-12.18.3.2511/forge-1.10.2-12.18.3.2511-installer.jar">12.18.3.2511</a>
                      </body>
                    </html>
                    """));
            }

            if (uri == "https://maven.minecraftforge.net/net/minecraftforge/forge/1.10.2-12.18.3.2511/forge-1.10.2-12.18.3.2511-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request, legacyInstallerBytes));

            throw new InvalidOperationException($"Unexpected request: {uri}");
        }

        private static HttpResponseMessage CreateJsonResponse(HttpRequestMessage request, string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(json)
            };
        }

        private static HttpResponseMessage CreateHtmlResponse(HttpRequestMessage request, string html)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(html)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request)
        {
            return CreateBinaryResponse(request, CreateModernForgeInstallerBytes());
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request, byte[]? content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content ?? "forge installer bytes"u8.ToArray())
            };
        }
    }
}
