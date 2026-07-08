using System.Net;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ForgeLoaderProviderTests : TestTempDirectory
{
    [Fact]
    public async Task ForgeLoaderProviderParsesVersionsFromOfficialCatalog()
    {
        var provider = CreateProvider();

        var versions = await provider.GetLoaderVersionsAsync("1.20.1");

        Assert.Equal(["47.4.20", "47.4.10"], versions.Select(version => version.Version));
        Assert.All(versions, version => Assert.True(version.IsStable));
    }

    [Fact]
    public void MergeFlattenedVersionNormalizesLegacyForgeMinecraftArguments()
    {
        var baseVersion = new JsonObject
        {
            ["id"] = "1.12.2",
            ["minecraftArguments"] = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --versionType ${version_type}"
        };
        var derivedVersion = new JsonObject
        {
            ["id"] = "forge-1.12.2-14.23.5.2860",
            ["inheritsFrom"] = "1.12.2",
            ["minecraftArguments"] = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --versionType Forge"
        };

        var merged = VersionJsonMergeHelper.MergeFlattenedVersion(baseVersion, derivedVersion, "RLCraft", "1.12.2");

        var arguments = merged["minecraftArguments"]!.GetValue<string>();
        Assert.Equal(1, CountArgument(arguments, "--gameDir"));
        Assert.Equal(1, CountArgument(arguments, "--assetsDir"));
        Assert.Equal(1, CountArgument(arguments, "--accessToken"));
        Assert.Contains("--tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker", arguments);
        Assert.Contains("--versionType Forge", arguments);
        Assert.DoesNotContain("--versionType ${version_type}", arguments);
    }

    [Fact]
    public async Task ForgeLoaderProviderInstallCreatesOnlyFinalVersionDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        var finalInstaller = new RecordingFinalVersionInstaller();

        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
        {
            return CreateSandboxForgeInstallAsync(gameDirectory, "forge-1.20.1-47.4.20", "1.20.1", "1.20.1-47.4.20");
        }), finalInstaller);

        var finalVersionName = await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var versionDirectories = Directory.GetDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal("1.20.1-forge-47.4.20", finalVersionName);
        Assert.Equal(["1.20.1", "1.20.1-forge-47.4.20"], versionDirectories);
        Assert.False(Directory.Exists(Path.Combine(versionsDirectory, "forge-1.20.1-47.4.20")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.1-forge-47.4.20", "1.20.1-forge-47.4.20.jar")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.1-forge-47.4.20", "win_args.txt")));
        Assert.True(File.Exists(Path.Combine(
            minecraftDirectory,
            "libraries",
            "net",
            "minecraftforge",
            "forge",
            "1.20.1-47.4.20",
            "forge-1.20.1-47.4.20-client.jar")));
        Assert.False(File.Exists(Path.Combine(minecraftDirectory, "launcher_profiles.json")));
        Assert.Equal("1.20.1-forge-47.4.20", finalInstaller.LastVersionName);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(versionsDirectory, "1.20.1-forge-47.4.20", "1.20.1-forge-47.4.20.json")));
        Assert.Equal("1.20.1-forge-47.4.20", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.1-forge-47.4.20", json.RootElement.GetProperty("jar").GetString());
        Assert.False(json.RootElement.TryGetProperty("inheritsFrom", out _));
        Assert.Equal("1.20.1", json.RootElement.GetProperty("launcher").GetProperty("minecraftVersion").GetString());
    }

    [Fact]
    public async Task ForgeLoaderProviderRunsInstallerInTempSandboxInsteadOfMainMinecraftDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");
        string? installerGameDirectory = null;
        var sawLauncherProfile = false;

        var provider = CreateProvider(new ScriptedForgeInstallerRunner((gameDirectory, _, _) =>
        {
            installerGameDirectory = gameDirectory;
            sawLauncherProfile = File.Exists(Path.Combine(gameDirectory, "launcher_profiles.json"));
            return CreateSandboxForgeInstallAsync(gameDirectory, "forge-1.20.1-47.4.20", "1.20.1", "1.20.1-47.4.20");
        }));

        await provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null);

        Assert.NotNull(installerGameDirectory);
        Assert.NotEqual(
            Path.GetFullPath(minecraftDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(installerGameDirectory!).TrimEnd(Path.DirectorySeparatorChar));
        Assert.StartsWith(Path.Combine(TempRoot, "launcher-forge"), installerGameDirectory!, StringComparison.OrdinalIgnoreCase);
        Assert.True(sawLauncherProfile);
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "forge-1.20.1-47.4.20")));
    }

    [Fact]
    public async Task ForgeLoaderProviderInstallCleansCreatedVersionDirectoriesWhenInstallerFails()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        await CreateVanillaVersionAsync(minecraftDirectory, "1.20.1");

        var provider = CreateProvider(new ScriptedForgeInstallerRunner(async (gameDirectory, _, _) =>
        {
            await CreateSandboxForgeInstallAsync(gameDirectory, "forge-1.20.1-47.4.20", "1.20.1", "1.20.1-47.4.20");
            throw new InvalidOperationException("No usable Java runtime was found for Forge installation.");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.InstallAsync(
            "1.20.1",
            minecraftDirectory,
            "1.20.1-forge-47.4.20",
            "47.4.20",
            progress: null));

        Assert.True(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.1")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "forge-1.20.1-47.4.20")));
        Assert.False(Directory.Exists(Path.Combine(minecraftDirectory, "versions", "1.20.1-forge-47.4.20")));
    }

    private static int CountArgument(string arguments, string argument)
    {
        return arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(token => string.Equals(token, argument, StringComparison.OrdinalIgnoreCase));
    }

    private ForgeLoaderProvider CreateProvider(
        IForgeInstallerRunner? runner = null,
        IFinalVersionInstaller? finalVersionInstaller = null)
    {
        return new ForgeLoaderProvider(
            new HttpClient(new ForgeHttpHandler()),
            runner ?? new NoOpForgeInstallerRunner(),
            finalVersionInstaller ?? new NoOpFinalVersionInstaller(),
            TempRoot);
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
        var forgeLibraryDirectory = Path.Combine(
            minecraftDirectory,
            "libraries",
            "net",
            "minecraftforge",
            "forge",
            combinedForgeVersion);
        Directory.CreateDirectory(forgeLibraryDirectory);
        File.WriteAllText(
            Path.Combine(forgeLibraryDirectory, $"forge-{combinedForgeVersion}-client.jar"),
            "patched forge client");
    }

    private static byte[] CreateLegacyForgeInstallerBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(profileEntry.Open()))
            {
                writer.Write(
                    """
                    {
                      "install": {
                        "profileName": "forge",
                        "target": "1.10.2-forge1.10.2-12.18.3.2511",
                        "path": "net.minecraftforge:forge:1.10.2-12.18.3.2511",
                        "version": "forge 1.10.2-12.18.3.2511",
                        "filePath": "forge-1.10.2-12.18.3.2511-universal.jar",
                        "minecraft": "1.10.2"
                      },
                      "versionInfo": {
                        "id": "1.10.2-forge1.10.2-12.18.3.2511",
                        "type": "release",
                        "minecraftArguments": "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --versionType Forge",
                        "mainClass": "net.minecraft.launchwrapper.Launch",
                        "inheritsFrom": "1.10.2",
                        "jar": "1.10.2",
                        "libraries": [
                          { "name": "net.minecraftforge:forge:1.10.2-12.18.3.2511", "url": "https://maven.minecraftforge.net/" },
                          { "name": "net.minecraft:launchwrapper:1.12" }
                        ]
                      }
                    }
                    """);
            }

            var forgeJarEntry = archive.CreateEntry("forge-1.10.2-12.18.3.2511-universal.jar");
            using var forgeJarWriter = new StreamWriter(forgeJarEntry.Open());
            forgeJarWriter.Write("legacy forge universal jar");
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

    private sealed class RecordingFinalVersionInstaller : IFinalVersionInstaller
    {
        public string? LastVersionName { get; private set; }

        public Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LastVersionName = versionName;
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyFallbackFinalVersionInstaller : IFinalVersionInstaller
    {
        public List<string> InstalledVersionNames { get; } = [];

        public async Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            InstalledVersionNames.Add(versionName);
            if (string.Equals(versionName, "1.10.2", StringComparison.OrdinalIgnoreCase))
                await CreateVanillaVersionAsync(gameDirectory, versionName);
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
            var uri = request.RequestUri!.AbsoluteUri;
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
            return CreateBinaryResponse(request, "forge installer bytes"u8.ToArray());
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
