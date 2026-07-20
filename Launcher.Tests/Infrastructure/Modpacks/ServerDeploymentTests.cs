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
using Launcher.Application.Services;
using Launcher.Domain.Models;
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
        Assert.False(File.Exists(Path.Combine(target, "eula.txt")));
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

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
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

    private sealed class ServerRuntimeHandler(byte[] serverBytes, string sha1) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            HttpContent content = url switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => new StringContent(
                    """{"versions":[{"id":"1.20.1","url":"https://piston-meta.mojang.com/v1/packages/test/1.20.1.json"}]}""",
                    Encoding.UTF8,
                    "application/json"),
                "https://piston-meta.mojang.com/v1/packages/test/1.20.1.json" => new StringContent(
                    JsonSerializer.Serialize(new
                    {
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
        LoaderKind loader) : HttpMessageHandler
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
            ? "https://meta.quiltmc.org/v3/versions/loader/1.20.1/1.0.0/server/json"
            : "https://meta.fabricmc.net/v2/versions/loader/1.20.1/1.0.0/server/json";
    }
}
