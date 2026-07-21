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

using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class NeoForgeLoaderProviderTests : TestTempDirectory
{
    [Fact]
    public async Task NeoForgeLoaderProviderPassesSelectedJavaPathToInstallerRunner()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");
        var expectedJavaPath = Path.Combine(TempRoot, "Selected Java", "bin", "java.exe");
        string? receivedJavaPath = null;
        var javaRuntimeResolver = new FixedJavaRuntimeResolver(expectedJavaPath);
        var provider = CreateProvider(
            new ScriptedForgeInstallerRunner((gameDirectory, javaPath, _) =>
            {
                receivedJavaPath = javaPath;
                return CreateSandboxNeoForgeInstallAsync(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237");
            }),
            javaRuntimeResolver: javaRuntimeResolver);

        await provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "1.20.4-neoforge-20.4.237",
            "20.4.237",
            progress: null);

        Assert.Equal(expectedJavaPath, receivedJavaPath);
        Assert.Equal(minecraftDirectory, javaRuntimeResolver.LastRequest?.MinecraftDirectory);
        Assert.Equal(DownloadSourcePreference.Official, javaRuntimeResolver.LastRequest?.DownloadSourcePreference);
        Assert.Equal(LoaderKind.NeoForge, javaRuntimeResolver.LastRequest?.Loader);
        Assert.Equal("20.4.237", javaRuntimeResolver.LastRequest?.LoaderVersion);
    }

    [Fact]
    public async Task NeoForgeLoaderProviderRejectsSuccessfulInstallerWithMissingProcessorOutput()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");
        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
        {
            CreateNeoForgeDerivedVersion(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237");
            return Task.CompletedTask;
        }));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "1.20.4-neoforge-20.4.237",
            "20.4.237",
            progress: null));

        Assert.Contains("processor output", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(
            minecraftDirectory,
            "versions",
            "1.20.4-neoforge-20.4.237")));
    }

    [Fact]
    public async Task NeoForgeLoaderProviderInstallDoesNotCreateRealVanillaBaseDirectoryWhenMissing()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");

        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
        {
            return CreateSandboxNeoForgeInstallAsync(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237");
        }));

        var finalVersionName = await provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "Imported NeoForge Pack",
            "20.4.237",
            progress: null);

        Assert.Equal("Imported NeoForge Pack", finalVersionName);
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.4")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "neoforge-20.4.237")));
        Assert.True(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "Imported NeoForge Pack")));
    }

    [Fact]
    public async Task NeoForgeLoaderProviderInstallCleansCreatedVersionDirectoriesWhenInstallerFails()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.4");

        var provider = CreateProvider(new ScriptedForgeInstallerRunner(async (gameDirectory, _, _) =>
        {
            await CreateSandboxNeoForgeInstallAsync(gameDirectory, "neoforge-20.4.237", "1.20.4", "20.4.237");
            throw new InvalidOperationException("No usable Java runtime was found for NeoForge installation.");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.InstallAsync(
            "1.20.4",
            minecraftDirectory,
            "1.20.4-neoforge-20.4.237",
            "20.4.237",
            progress: null));

        Assert.True(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.4")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "neoforge-20.4.237")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.4-neoforge-20.4.237")));
    }

    private NeoForgeLoaderProvider CreateProvider(
        IForgeInstallerRunner? runner = null,
        IFinalVersionInstaller? finalVersionInstaller = null,
        ILoaderInstallerJavaRuntimeResolver? javaRuntimeResolver = null)
    {
        return new NeoForgeLoaderProvider(
            new HttpClient(new NeoForgeHttpHandler()),
            runner ?? new NoOpForgeInstallerRunner(),
            finalVersionInstaller ?? new NoOpFinalVersionInstaller(),
            TempRoot,
            javaRuntimeResolver: javaRuntimeResolver ?? new FixedJavaRuntimeResolver());
    }

    private static async Task CreateSandboxNeoForgeInstallAsync(
        string minecraftDirectory,
        string versionName,
        string inheritsFrom,
        string loaderVersion)
    {
        await CreateVanillaVersionAsync(minecraftDirectory, inheritsFrom);
        CreateNeoForgeDerivedVersion(minecraftDirectory, versionName, inheritsFrom, loaderVersion);
        CreateGeneratedNeoForgeLibrary(minecraftDirectory, loaderVersion, includeUniversal: true);
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

    private static void CreateNeoForgeDerivedVersion(string minecraftDirectory, string versionName, string inheritsFrom, string loaderVersion)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var versionJson = new JsonObject
        {
            ["id"] = versionName,
            ["inheritsFrom"] = inheritsFrom,
            ["mainClass"] = "cpw.mods.modlauncher.Launcher",
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray(
                    "--fml.neoForgeVersion",
                    loaderVersion,
                    "--fml.mcVersion",
                    inheritsFrom)
            },
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = $"net.neoforged:neoforge:{loaderVersion}" }
            }
        };

        File.WriteAllText(
            Path.Combine(versionDirectory, $"{versionName}.json"),
            versionJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(versionDirectory, "win_args.txt"),
            "--launchTarget neoforge_client");
    }

    private static void CreateGeneratedNeoForgeLibrary(
        string minecraftDirectory,
        string loaderVersion,
        bool includeUniversal)
    {
        var libraryDirectory = Path.Combine(
            minecraftDirectory,
            "libraries",
            "net",
            "neoforged",
            "neoforge",
            loaderVersion);
        Directory.CreateDirectory(libraryDirectory);
        File.WriteAllText(
            Path.Combine(libraryDirectory, $"neoforge-{loaderVersion}-client.jar"),
            "patched neoforge client");
        if (includeUniversal)
        {
            File.WriteAllText(
                Path.Combine(libraryDirectory, $"neoforge-{loaderVersion}-universal.jar"),
                "universal neoforge runtime");
        }
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

    private sealed class NeoForgeHttpHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            var uri = request.RequestUri!.AbsoluteUri
                .Replace("https://bmclapi2.bangbang93.com/maven/", "https://maven.neoforged.net/releases/", StringComparison.OrdinalIgnoreCase);
            if (uri == "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml")
            {
                return Task.FromResult(CreateTextResponse(request, """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <metadata>
                      <groupId>net.neoforged</groupId>
                      <artifactId>neoforge</artifactId>
                      <versioning>
                        <versions>
                          <version>20.4.235-beta</version>
                          <version>20.4.236</version>
                          <version>20.4.237</version>
                          <version>20.6.115</version>
                          <version>21.1.234</version>
                        </versions>
                      </versioning>
                    </metadata>
                    """));
            }

            if (uri == "https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.237/neoforge-20.4.237-installer.jar")
                return Task.FromResult(CreateBinaryResponse(request, CreateInstallerBytes()));
            if (uri == "https://maven.neoforged.net/releases/net/neoforged/neoforge/20.4.237/neoforge-20.4.237-universal.jar")
                return Task.FromResult(CreateTextResponse(request, "universal neoforge runtime"));

            throw new InvalidOperationException($"Unexpected request: {uri}");
        }

        private static HttpResponseMessage CreateTextResponse(HttpRequestMessage request, string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(HttpRequestMessage request, byte[] content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            };
        }
    }

    private static byte[] CreateInstallerBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write(
                """
                {
                  "data": {
                    "PATCHED": { "client": "[net.neoforged:neoforge:20.4.237:client]" }
                  },
                  "libraries": [
                    {
                      "name": "net.neoforged:neoforge:20.4.237:universal",
                      "downloads": {
                        "artifact": {
                          "path": "net/neoforged/neoforge/20.4.237/neoforge-20.4.237-universal.jar",
                          "sha1": "bb0166e91991e502fc8d8daf77eedced1b734f6a",
                          "size": 26
                        }
                      }
                    }
                  ],
                  "processors": [
                    { "args": ["--clean", "{MC_SRG}", "--output", "{PATCHED}"] }
                  ]
                }
                """);
            }
            var versionEntry = archive.CreateEntry("version.json");
            using var versionWriter = new StreamWriter(versionEntry.Open());
            versionWriter.Write(
                """
                {
                  "libraries": [
                    {
                      "name": "net.neoforged:neoforge:20.4.237:universal",
                      "downloads": {
                        "artifact": {
                          "path": "net/neoforged/neoforge/20.4.237/neoforge-20.4.237-universal.jar",
                          "sha1": "bb0166e91991e502fc8d8daf77eedced1b734f6a",
                          "size": 26
                        }
                      }
                    }
                  ]
                }
                """);
        }
        return stream.ToArray();
    }
}
