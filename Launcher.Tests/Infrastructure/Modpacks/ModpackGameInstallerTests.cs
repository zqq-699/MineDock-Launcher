using System.Net;
using System.Text.Json;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ModpackGameInstallerTests : TestTempDirectory
{
    [Fact]
    public async Task InstallLoaderAsyncForVanillaCreatesOnlyFinalVersionDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var installer = new ModpackGameInstaller(
            [],
            new NoOpVanillaSharedRuntimePreparer(),
            new RecordingFinalVersionInstaller(),
            new HttpClient(new VanillaInstallHandler()),
            tempRootDirectory: TempRoot);

        var finalVersionName = await installer.InstallLoaderAsync(
            "1.20.2",
            LoaderKind.Vanilla,
            loaderVersion: null,
            minecraftDirectory,
            "Vanilla Pack",
            progress: null);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        Assert.Equal("Vanilla Pack", finalVersionName);
        Assert.Equal(["Vanilla Pack"], Directory.GetDirectories(versionsDirectory).Select(Path.GetFileName));
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "1.20.2")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "Vanilla Pack", "Vanilla Pack.json")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "Vanilla Pack", "Vanilla Pack.jar")));
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "libraries", "com", "example", "installed", "1.0.0", "installed-1.0.0.jar")));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(versionsDirectory, "Vanilla Pack", "Vanilla Pack.json")));
        Assert.Equal("Vanilla Pack", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.2", json.RootElement.GetProperty("launcher").GetProperty("minecraftVersion").GetString());
    }

    [Fact]
    public async Task InstallLoaderAsyncForFabricCreatesOnlyFinalVersionDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var installer = new ModpackGameInstaller(
            [],
            new NoOpVanillaSharedRuntimePreparer(),
            new RecordingFinalVersionInstaller(),
            new HttpClient(new FabricInstallHandler()),
            tempRootDirectory: TempRoot);

        var finalVersionName = await installer.InstallLoaderAsync(
            "1.20.2",
            LoaderKind.Fabric,
            "0.19.3",
            minecraftDirectory,
            "Fabric Pack",
            progress: null);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        Assert.Equal("Fabric Pack", finalVersionName);
        Assert.Equal(["Fabric Pack"], Directory.GetDirectories(versionsDirectory).Select(Path.GetFileName));
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "1.20.2")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "Fabric Pack", "Fabric Pack.json")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "Fabric Pack", "Fabric Pack.jar")));
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "assets", "log_configs", "client.xml")));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(versionsDirectory, "Fabric Pack", "Fabric Pack.json")));
        Assert.Equal("Fabric Pack", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.2", json.RootElement.GetProperty("launcher").GetProperty("minecraftVersion").GetString());
    }

    private sealed class NoOpVanillaSharedRuntimePreparer : IVanillaSharedRuntimePreparer
    {
        public Task PrepareAsync(
            string minecraftVersion,
            string targetMinecraftDirectory,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFinalVersionInstaller : IFinalVersionInstaller
    {
        public Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            var libraryPath = Path.Combine(
                gameDirectory,
                "libraries",
                "com",
                "example",
                "installed",
                "1.0.0",
                "installed-1.0.0.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
            File.WriteAllText(libraryPath, "installed");

            var logConfigPath = Path.Combine(gameDirectory, "assets", "log_configs", "client.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(logConfigPath)!);
            File.WriteAllText(logConfigPath, "<xml />");
            return Task.CompletedTask;
        }
    }

    private sealed class VanillaInstallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsoluteUri switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => """
                    {
                      "versions": [
                        {
                          "id": "1.20.2",
                          "url": "https://example.test/mojang/1.20.2.json"
                        }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.json" => """
                    {
                      "id": "1.20.2",
                      "type": "release",
                      "downloads": {
                        "client": {
                          "url": "https://example.test/mojang/1.20.2.jar"
                        }
                      },
                      "libraries": [
                        { "name": "com.mojang:patchy:2.2.10" }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.jar" => null,
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content is null
                    ? new ByteArrayContent("fake jar"u8.ToArray())
                    : new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FabricInstallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsoluteUri switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => """
                    {
                      "versions": [
                        {
                          "id": "1.20.2",
                          "url": "https://example.test/mojang/1.20.2.json"
                        }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.json" => """
                    {
                      "id": "1.20.2",
                      "type": "release",
                      "downloads": {
                        "client": {
                          "url": "https://example.test/mojang/1.20.2.jar"
                        }
                      },
                      "libraries": [
                        { "name": "com.mojang:patchy:2.2.10" }
                      ],
                      "arguments": {
                        "game": [ "--username", "${auth_player_name}" ],
                        "jvm": [ "-Djava.library.path=${natives_directory}" ]
                      }
                    }
                    """,
                "https://meta.fabricmc.net/v2/versions/loader/1.20.2/0.19.3/profile/json" => """
                    {
                      "id": "fabric-loader-0.19.3-1.20.2",
                      "inheritsFrom": "1.20.2",
                      "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
                      "libraries": [
                        { "name": "net.fabricmc:fabric-loader:0.19.3" }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.jar" => null,
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content is null
                    ? new ByteArrayContent("fake jar"u8.ToArray())
                    : new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }
}
