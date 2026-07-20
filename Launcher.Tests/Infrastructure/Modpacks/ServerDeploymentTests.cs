/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class ServerDeploymentTests : TestTempDirectory
{
    [Fact]
    public async Task TransactionCommitsAtomicallyAndRejectsExistingDirectory()
    {
        var parent = Path.Combine(TempRoot, "parent");
        Directory.CreateDirectory(parent);
        var service = new ServerDeploymentTransactionService();
        await using (var transaction = await service.BeginAsync(parent, "Pack"))
        {
            await File.WriteAllTextAsync(Path.Combine(transaction.StagingDirectory, "server.jar"), "server");
            await transaction.CommitAsync();
        }

        Assert.True(File.Exists(Path.Combine(parent, "Pack", "server.jar")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.Combine(parent, "Pack")),
            path => Path.GetFileName(path).StartsWith(
                ServerDeploymentTransactionService.MarkerFileNamePrefix,
                StringComparison.Ordinal));
        await Assert.ThrowsAsync<ServerDeploymentDirectoryExistsException>(() => service.BeginAsync(parent, "Pack"));
    }

    [Fact]
    public async Task TransactionAbortRemovesOnlyOwnedStagingDirectory()
    {
        var parent = Path.Combine(TempRoot, "parent");
        Directory.CreateDirectory(parent);
        var unrelated = Path.Combine(parent, "unrelated");
        Directory.CreateDirectory(unrelated);
        var transaction = await new ServerDeploymentTransactionService().BeginAsync(parent, "Pack");
        var staging = transaction.StagingDirectory;

        await transaction.AbortAsync();

        Assert.False(Directory.Exists(staging));
        Assert.True(Directory.Exists(unrelated));
        await transaction.DisposeAsync();
    }

    [Fact]
    public async Task TransactionCommitRacePreservesExistingFinalDirectory()
    {
        var parent = Path.Combine(TempRoot, "parent");
        Directory.CreateDirectory(parent);
        await using var transaction = await new ServerDeploymentTransactionService().BeginAsync(parent, "Pack");
        var finalDirectory = Path.Combine(parent, "Pack");
        Directory.CreateDirectory(finalDirectory);
        await File.WriteAllTextAsync(Path.Combine(finalDirectory, "owned-by-user.txt"), "keep");

        await Assert.ThrowsAsync<ServerDeploymentDirectoryExistsException>(() => transaction.CommitAsync());
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(finalDirectory, "owned-by-user.txt")));
        Assert.True(Directory.Exists(transaction.StagingDirectory));

        await transaction.AbortAsync();
        Assert.False(Directory.Exists(transaction.StagingDirectory));
        Assert.True(Directory.Exists(finalDirectory));
    }

    [Fact]
    public async Task CurseForgeExtractorFlattensSingleTopLevelDirectoryAndPreservesScripts()
    {
        Directory.CreateDirectory(TempRoot);
        var archivePath = Path.Combine(TempRoot, "server.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "Server/mods/mod.jar", "mod");
            AddEntry(archive, "Server/run.bat", "java -jar server.jar");
            AddEntry(archive, "readme.txt", "readme");
        }
        var target = Path.Combine(TempRoot, "target");
        Directory.CreateDirectory(target);

        await new CurseForgeServerPackExtractor().ExtractAsync(archivePath, target);

        Assert.True(File.Exists(Path.Combine(target, "mods", "mod.jar")));
        Assert.True(File.Exists(Path.Combine(target, "run.bat")));
        Assert.True(File.Exists(Path.Combine(target, "readme.txt")));
        Assert.False(Directory.Exists(Path.Combine(target, "Server")));
    }

    [Fact]
    public async Task CurseForgeExtractorRejectsPathTraversal()
    {
        Directory.CreateDirectory(TempRoot);
        var archivePath = Path.Combine(TempRoot, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            AddEntry(archive, "../outside.txt", "bad");
        var target = Path.Combine(TempRoot, "target");
        Directory.CreateDirectory(target);

        await Assert.ThrowsAsync<ModpackImportException>(() =>
            new CurseForgeServerPackExtractor().ExtractAsync(archivePath, target));

        Assert.False(File.Exists(Path.Combine(TempRoot, "outside.txt")));
    }

    [Fact]
    public async Task CurseForgeExtractorRejectsDuplicateTargets()
    {
        Directory.CreateDirectory(TempRoot);
        var archivePath = Path.Combine(TempRoot, "duplicate.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "mods/example.jar", "first");
            AddEntry(archive, "mods/example.jar", "second");
        }
        var target = Path.Combine(TempRoot, "target");
        Directory.CreateDirectory(target);

        await Assert.ThrowsAsync<ModpackImportException>(() =>
            new CurseForgeServerPackExtractor().ExtractAsync(archivePath, target));
    }

    [Fact]
    public async Task CurseForgeExtractorRejectsSymbolicLinkEntries()
    {
        Directory.CreateDirectory(TempRoot);
        var archivePath = Path.Combine(TempRoot, "symlink.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("server-link");
            entry.ExternalAttributes = 0xA000 << 16;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("server.jar");
        }
        var target = Path.Combine(TempRoot, "target");
        Directory.CreateDirectory(target);

        await Assert.ThrowsAsync<ModpackImportException>(() =>
            new CurseForgeServerPackExtractor().ExtractAsync(archivePath, target));
    }

    [Fact]
    public async Task VanillaRuntimeDownloadsServerJarAndDoesNotAcceptEula()
    {
        var serverBytes = Encoding.UTF8.GetBytes("server-jar");
        var sha1 = Convert.ToHexString(SHA1.HashData(serverBytes)).ToLowerInvariant();
        var handler = new ServerRuntimeHandler(serverBytes, sha1);
        var target = Path.Combine(TempRoot, "runtime");
        var installer = new ServerRuntimeInstaller(
            new HttpClient(handler),
            new FixedSettingsService());

        await installer.InstallAsync(
            new PreparedModpack
            {
                Environment = ModpackInstallEnvironment.Server,
                MinecraftVersion = "1.20.1",
                Loader = LoaderKind.Vanilla
            },
            target);

        Assert.Equal(serverBytes, await File.ReadAllBytesAsync(Path.Combine(target, "minecraft_server.1.20.1.jar")));
        Assert.True(File.Exists(Path.Combine(target, "LaunchServer.bat")));
        Assert.True(File.Exists(Path.Combine(target, "LaunchServer.sh")));
        Assert.False(File.Exists(Path.Combine(target, "log4j2.xml")));
        Assert.DoesNotContain(
            ServerLog4ShellMitigation.JvmArguments,
            await File.ReadAllTextAsync(Path.Combine(target, "LaunchServer.bat")));
        Assert.False(File.Exists(Path.Combine(target, "eula.txt")));
    }

    [Fact]
    public async Task VulnerableVanillaRuntimeWritesMitigationConfigAndLaunchArguments()
    {
        var serverBytes = Encoding.UTF8.GetBytes("server-jar");
        var sha1 = Convert.ToHexString(SHA1.HashData(serverBytes)).ToLowerInvariant();
        var handler = new ServerRuntimeHandler(
            serverBytes,
            sha1,
            "1.12.2",
            "2017-09-18T08:39:46+00:00");
        var target = Path.Combine(TempRoot, "vulnerable-runtime");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "start.bat"), "java -jar custom.jar");
        var installer = new ServerRuntimeInstaller(new HttpClient(handler), new FixedSettingsService());

        await installer.InstallAsync(
            new PreparedModpack
            {
                Environment = ModpackInstallEnvironment.Server,
                MinecraftVersion = "1.12.2",
                Loader = LoaderKind.Vanilla
            },
            target);

        Assert.True(File.Exists(Path.Combine(target, "log4j2.xml")));
        Assert.Contains("%msg{nolookups}", await File.ReadAllTextAsync(Path.Combine(target, "log4j2.xml")));
        Assert.Contains(
            ServerLog4ShellMitigation.JvmArguments,
            await File.ReadAllTextAsync(Path.Combine(target, "LaunchServer.bat")));
        Assert.Contains(
            ServerLog4ShellMitigation.JvmArguments,
            await File.ReadAllTextAsync(Path.Combine(target, "LaunchServer.sh")));
        Assert.Equal("java -jar custom.jar", await File.ReadAllTextAsync(Path.Combine(target, "start.bat")));
        Assert.False(File.Exists(Path.Combine(target, "eula.txt")));
    }

    [Fact]
    public async Task VulnerableForgeRuntimeAppendsArgumentsToInstallerConfigurationOnce()
    {
        var target = Path.Combine(TempRoot, "forge-runtime");
        Directory.CreateDirectory(target);
        var userJvmArguments = Path.Combine(target, "user_jvm_args.txt");
        File.WriteAllText(userJvmArguments, "-Xmx2G", new UTF8Encoding(false));
        var metadata = new JsonObject
        {
            ["releaseTime"] = "2017-09-18T08:39:46+00:00"
        };

        var first = ServerLog4ShellMitigation.Apply(metadata, LoaderKind.Forge, target);
        var second = ServerLog4ShellMitigation.Apply(metadata, LoaderKind.Forge, target);

        Assert.Equal(ServerLog4ShellMitigation.JvmArguments, first);
        Assert.Equal(first, second);
        Assert.Equal(
            1,
            (await File.ReadAllTextAsync(userJvmArguments)).Split(
                ServerLog4ShellMitigation.JvmArguments,
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "net.minecraftforge.server.terminalconsole",
            File.ReadAllText(Path.Combine(target, "log4j2.xml")));
    }

    [Theory]
    [InlineData("2013-10-22T15:04:04+00:00", false)]
    [InlineData("2013-10-22T15:04:05+00:00", true)]
    [InlineData("2021-12-10T08:22:59+00:00", true)]
    [InlineData("2021-12-10T08:23:00+00:00", false)]
    public void Log4ShellVulnerabilityUsesAtLauncherReleaseTimeBoundaries(string releaseTime, bool expected)
    {
        Assert.Equal(expected, ServerLog4ShellMitigation.IsVulnerable(DateTimeOffset.Parse(releaseTime)));
    }

    [Theory]
    [InlineData(LoaderKind.Fabric, "fabric-server-launch.jar", "fabric-server-launcher.properties")]
    [InlineData(LoaderKind.Quilt, "quilt-server-launch.jar", "quilt-server-launcher.properties")]
    public async Task ProfileLoaderRuntimeCreatesLauncherJarAndProperties(
        LoaderKind loader,
        string launcherJar,
        string propertiesFile)
    {
        var serverBytes = Encoding.UTF8.GetBytes("server-jar");
        var libraryBytes = Encoding.UTF8.GetBytes("loader-library");
        var handler = new ProfileRuntimeHandler(serverBytes, libraryBytes, loader);
        var target = Path.Combine(TempRoot, loader.ToString());
        var installer = new ServerRuntimeInstaller(new HttpClient(handler), new FixedSettingsService());

        await installer.InstallAsync(
            new PreparedModpack
            {
                Environment = ModpackInstallEnvironment.Server,
                MinecraftVersion = "1.20.1",
                Loader = loader,
                LoaderVersion = "1.0.0"
            },
            target);

        Assert.True(File.Exists(Path.Combine(target, launcherJar)));
        Assert.Contains(
            "serverJar=minecraft_server.1.20.1.jar",
            await File.ReadAllTextAsync(Path.Combine(target, propertiesFile)));
        Assert.True(File.Exists(Path.Combine(target, "libraries", "example", "loader", "1.0", "loader-1.0.jar")));
        Assert.True(File.Exists(Path.Combine(target, "LaunchServer.bat")));
        Assert.False(File.Exists(Path.Combine(target, "eula.txt")));
        using var archive = ZipFile.OpenRead(Path.Combine(target, launcherJar));
        Assert.NotNull(archive.GetEntry("META-INF/MANIFEST.MF"));
    }

    [Fact]
    public async Task LegacyFabricRuntimeShadesDownloadedLoaderLibraries()
    {
        var serverBytes = Encoding.UTF8.GetBytes("server-jar");
        var libraryBytes = CreateJarBytes(
            "net/fabricmc/loader/launch/server/FabricServerLauncher.class",
            [1, 2, 3]);
        var handler = new ProfileRuntimeHandler(serverBytes, libraryBytes, LoaderKind.Fabric, "0.12.5");
        var target = Path.Combine(TempRoot, "legacy-fabric");
        var installer = new ServerRuntimeInstaller(new HttpClient(handler), new FixedSettingsService());

        await installer.InstallAsync(
            new PreparedModpack
            {
                Environment = ModpackInstallEnvironment.Server,
                MinecraftVersion = "1.20.1",
                Loader = LoaderKind.Fabric,
                LoaderVersion = "0.12.5"
            },
            target);

        using var launcher = ZipFile.OpenRead(Path.Combine(target, "fabric-server-launch.jar"));
        Assert.NotNull(launcher.GetEntry("net/fabricmc/loader/launch/server/FabricServerLauncher.class"));
        var manifestEntry = launcher.GetEntry("META-INF/MANIFEST.MF")!;
        using var reader = new StreamReader(manifestEntry.Open());
        Assert.DoesNotContain("Class-Path:", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(LoaderKind.Forge, "1.20.1", "47.2.0", "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar")]
    [InlineData(LoaderKind.Forge, "1.20.1", "1.20.1-47.2.0", "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar")]
    [InlineData(LoaderKind.NeoForge, "1.20.1", "47.1.106", "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar")]
    [InlineData(LoaderKind.NeoForge, "1.21.1", "1.21.1-21.1.172", "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.172/neoforge-21.1.172-installer.jar")]
    public void ForgeLikeInstallerCoordinatesAreNormalized(
        LoaderKind loader,
        string minecraftVersion,
        string loaderVersion,
        string expectedUrl)
    {
        var artifact = ServerRuntimeInstaller.ResolveForgeLikeInstallerArtifact(new PreparedModpack
        {
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion
        });

        Assert.Equal(expectedUrl, artifact.Url);
    }

    [Fact]
    public async Task ForgeLikeRuntimeRejectsSuccessfulInstallerWithMissingServerProcessorOutput()
    {
        var target = Path.Combine(TempRoot, "forge-missing-output");
        var installerBytes = CreateForgeServerInstaller();
        var handler = new ForgeServerRuntimeHandler(installerBytes);
        var runner = new ScriptedForgeServerInstallerRunner(directory =>
            WriteModernForgeServerEntrypoint(directory, writeProcessorOutput: false));
        var installer = new ServerRuntimeInstaller(
            new HttpClient(handler),
            new FixedSettingsService(),
            new FixedLoaderJavaRuntimeResolver(),
            runner);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            CreateForgeServerModpack(),
            target));

        Assert.Contains("processor output", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(runner.WasRun);
        Assert.Contains(handler.RequestedUrls, url => url.EndsWith("-installer.jar.sha1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForgeLikeRuntimeDoesNotExecuteInstallerWithoutValidChecksumMetadata()
    {
        var target = Path.Combine(TempRoot, "forge-invalid-checksum");
        var installerBytes = CreateForgeServerInstaller();
        var handler = new ForgeServerRuntimeHandler(installerBytes, checksumOverride: "not-a-sha1");
        var runner = new ScriptedForgeServerInstallerRunner(directory =>
            WriteModernForgeServerEntrypoint(directory, writeProcessorOutput: true));
        var installer = new ServerRuntimeInstaller(
            new HttpClient(handler),
            new FixedSettingsService(),
            new FixedLoaderJavaRuntimeResolver(),
            runner);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            CreateForgeServerModpack(),
            target));

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runner.WasRun);
    }

    [Fact]
    public async Task ForgeLikeRuntimeCommitsOnlyAfterCompleteServerClosureAndEntrypointValidation()
    {
        var target = Path.Combine(TempRoot, "forge-complete");
        var installerBytes = CreateForgeServerInstaller();
        var handler = new ForgeServerRuntimeHandler(installerBytes);
        var runner = new ScriptedForgeServerInstallerRunner(directory =>
            WriteModernForgeServerEntrypoint(directory, writeProcessorOutput: true));
        var installer = new ServerRuntimeInstaller(
            new HttpClient(handler),
            new FixedSettingsService(),
            new FixedLoaderJavaRuntimeResolver(),
            runner);

        await installer.InstallAsync(CreateForgeServerModpack(), target);

        Assert.True(File.Exists(Path.Combine(target, "run.bat")));
        Assert.True(File.Exists(Path.Combine(target, "run.sh")));
        Assert.True(File.Exists(Path.Combine(
            target,
            "libraries",
            "com",
            "example",
            "server-output",
            "1.0",
            "server-output-1.0.jar")));
        Assert.False(File.Exists(Path.Combine(target, "eula.txt")));
    }

    [Fact]
    public async Task ForgeLikeRuntimeRejectsArgumentsFileWithMissingBundledLibrary()
    {
        var target = Path.Combine(TempRoot, "forge-missing-bundled-library");
        var installerBytes = CreateForgeServerInstaller();
        var handler = new ForgeServerRuntimeHandler(installerBytes);
        var runner = new ScriptedForgeServerInstallerRunner(directory =>
            WriteModernForgeServerEntrypoint(
                directory,
                writeProcessorOutput: true,
                writeBundledLibrary: false));
        var installer = new ServerRuntimeInstaller(
            new HttpClient(handler),
            new FixedSettingsService(),
            new FixedLoaderJavaRuntimeResolver(),
            runner);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
            CreateForgeServerModpack(),
            target));

        Assert.Contains("missing library JAR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static byte[] CreateJarBytes(string path, byte[] content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(path);
            using var destination = entry.Open();
            destination.Write(content);
        }
        return stream.ToArray();
    }

    private static PreparedModpack CreateForgeServerModpack() => new()
    {
        Environment = ModpackInstallEnvironment.Server,
        MinecraftVersion = "1.20.1",
        Loader = LoaderKind.Forge,
        LoaderVersion = "1.20.1-47.2.0"
    };

    private static byte[] CreateForgeServerInstaller()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "install_profile.json", """
                {
                  "data": {
                    "OUTPUT": {
                      "client": "[com.example:client-output:1.0]",
                      "server": "[com.example:server-output:1.0]"
                    }
                  },
                  "processors": [
                    {
                      "sides": ["server"],
                      "jar": "com.example:processor:1.0",
                      "args": ["--output", "{OUTPUT}"]
                    }
                  ]
                }
                """);
            AddEntry(archive, "version.json", """{ "libraries": [] }""");
            AddEntry(archive, "maven/com/example/processor/1.0/processor-1.0.jar", "processor");
        }
        return stream.ToArray();
    }

    private static void WriteModernForgeServerEntrypoint(
        string targetDirectory,
        bool writeProcessorOutput,
        bool writeBundledLibrary = true)
    {
        var argsDirectory = Path.Combine(
            targetDirectory,
            "libraries",
            "net",
            "minecraftforge",
            "forge",
            "1.20.1-47.2.0");
        Directory.CreateDirectory(argsDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "user_jvm_args.txt"), string.Empty);
        const string bundledLibrary = "libraries/com/example/bootstrap/1.0/bootstrap-1.0.jar";
        File.WriteAllText(Path.Combine(argsDirectory, "win_args.txt"), $"-p {bundledLibrary}");
        File.WriteAllText(Path.Combine(argsDirectory, "unix_args.txt"), $"-p {bundledLibrary}");
        File.WriteAllText(
            Path.Combine(targetDirectory, "run.bat"),
            "@echo off\r\njava @user_jvm_args.txt @libraries/net/minecraftforge/forge/1.20.1-47.2.0/win_args.txt %*");
        File.WriteAllText(
            Path.Combine(targetDirectory, "run.sh"),
            "java @user_jvm_args.txt @libraries/net/minecraftforge/forge/1.20.1-47.2.0/unix_args.txt \"$@\"");

        if (writeBundledLibrary)
            WriteTestJar(Path.Combine(targetDirectory, bundledLibrary.Replace('/', Path.DirectorySeparatorChar)));

        if (!writeProcessorOutput)
            return;
        var output = Path.Combine(
            targetDirectory,
            "libraries",
            "com",
            "example",
            "server-output",
            "1.0",
            "server-output-1.0.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "generated");
    }

    private static void WriteTestJar(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(archive, "META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\n\r\n");
    }

    private sealed class FixedSettingsService : ISettingsService
    {
        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LauncherSettings
            {
                DownloadSourcePreference = DownloadSourcePreference.Official,
                DownloadSpeedLimitMbPerSecond = 0
            });

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ServerRuntimeHandler(
        byte[] serverBytes,
        string sha1,
        string version = "1.20.1",
        string releaseTime = "2023-06-12T13:25:51+00:00") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            HttpContent content = url switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        versions = new[]
                        {
                            new
                            {
                                id = version,
                                url = $"https://piston-meta.mojang.com/v1/packages/test/{version}.json",
                                releaseTime
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"),
                var metadataUrl when metadataUrl == $"https://piston-meta.mojang.com/v1/packages/test/{version}.json" => new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        releaseTime,
                        downloads = new
                        {
                            server = new
                            {
                                url = "https://piston-data.mojang.com/v1/objects/test/server.jar",
                                sha1,
                                size = serverBytes.Length
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"),
                "https://piston-data.mojang.com/v1/objects/test/server.jar" => new ByteArrayContent(serverBytes),
                _ => throw new InvalidOperationException($"Unexpected request: {url}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        }
    }

    private sealed class ProfileRuntimeHandler(
        byte[] serverBytes,
        byte[] libraryBytes,
        LoaderKind loader,
        string loaderVersion = "1.0.0") : HttpMessageHandler
    {
        private readonly string serverSha1 = Convert.ToHexString(SHA1.HashData(serverBytes)).ToLowerInvariant();
        private readonly string librarySha1 = Convert.ToHexString(SHA1.HashData(libraryBytes)).ToLowerInvariant();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            HttpContent content;
            if (url == "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")
            {
                content = new StringContent(
                    """{"versions":[{"id":"1.20.1","url":"https://piston-meta.mojang.com/v1/packages/test/1.20.1.json"}]}""",
                    Encoding.UTF8,
                    "application/json");
            }
            else if (url == "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json")
            {
                content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        releaseTime = "2023-06-12T13:25:51+00:00",
                        downloads = new
                        {
                            server = new
                            {
                                url = "https://piston-data.mojang.com/v1/objects/test/server.jar",
                                sha1 = serverSha1,
                                size = serverBytes.Length
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json");
            }
            else if (url == "https://piston-data.mojang.com/v1/objects/test/server.jar")
            {
                content = new ByteArrayContent(serverBytes);
            }
            else if (url == ProfileUrl())
            {
                content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        mainClass = "example.server.Main",
                        launcherMainClass = "example.server.Launcher",
                        libraries = new[]
                        {
                            new
                            {
                                name = "example:loader:1.0",
                                downloads = new
                                {
                                    artifact = new
                                    {
                                        path = "example/loader/1.0/loader-1.0.jar",
                                        url = "https://libraries.test/loader.jar",
                                        sha1 = librarySha1,
                                        size = libraryBytes.Length
                                    }
                                }
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json");
            }
            else if (url == "https://libraries.test/loader.jar")
            {
                content = new ByteArrayContent(libraryBytes);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected request: {url}");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        }

        private string ProfileUrl() => loader is LoaderKind.Quilt
            ? $"https://meta.quiltmc.org/v3/versions/loader/1.20.1/{loaderVersion}/server/json"
            : $"https://meta.fabricmc.net/v2/versions/loader/1.20.1/{loaderVersion}/server/json";
    }

    private sealed class FixedLoaderJavaRuntimeResolver : ILoaderInstallerJavaRuntimeResolver
    {
        public Task<JavaRuntimeInfo> ResolveAsync(
            LoaderInstallerJavaRuntimeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new JavaRuntimeInfo(
                "Test Java",
                "17.0.1",
                17,
                "x64",
                "C:\\Java\\bin\\java.exe",
                "C:\\Java",
                "Test"));
    }

    private sealed class ScriptedForgeServerInstallerRunner(Action<string> install) : IForgeInstallerRunner
    {
        public bool WasRun { get; private set; }

        public Task RunInstallerAsync(
            string javaExecutablePath,
            string installerJarPath,
            string minecraftDirectory,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RunServerInstallerAsync(
            string javaExecutablePath,
            string installerJarPath,
            string serverDirectory,
            CancellationToken cancellationToken)
        {
            WasRun = true;
            install(serverDirectory);
            return Task.CompletedTask;
        }
    }

    private sealed class ForgeServerRuntimeHandler(
        byte[] installerBytes,
        string? checksumOverride = null) : HttpMessageHandler
    {
        private static readonly byte[] ServerBytes = Encoding.UTF8.GetBytes("server-jar");
        private readonly string installerSha1 = Convert.ToHexString(SHA1.HashData(installerBytes)).ToLowerInvariant();
        private readonly string serverSha1 = Convert.ToHexString(SHA1.HashData(ServerBytes)).ToLowerInvariant();

        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            RequestedUrls.Add(url);
            HttpContent content = url switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => new StringContent(
                    """{"versions":[{"id":"1.20.1","url":"https://piston-meta.mojang.com/v1/packages/test/1.20.1.json"}]}""",
                    Encoding.UTF8,
                    "application/json"),
                "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json" => new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        releaseTime = "2023-06-12T13:25:51+00:00",
                        downloads = new
                        {
                            server = new
                            {
                                url = "https://piston-data.mojang.com/v1/objects/test/server.jar",
                                sha1 = serverSha1,
                                size = ServerBytes.Length
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"),
                "https://piston-data.mojang.com/v1/objects/test/server.jar" => new ByteArrayContent(ServerBytes),
                "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar.sha1" => new StringContent(checksumOverride ?? installerSha1),
                "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar" => new ByteArrayContent(installerBytes),
                _ => throw new InvalidOperationException($"Unexpected request: {url}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        }
    }
}
