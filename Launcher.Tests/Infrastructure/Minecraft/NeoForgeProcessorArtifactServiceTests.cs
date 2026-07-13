/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class NeoForgeProcessorArtifactServiceTests : TestTempDirectory
{
    private const string MappingsPath = "net/neoforged/neoform/1.20.4-build/neoform-1.20.4-build-mappings.txt";
    private const string PatchedPath = "net/neoforged/neoforge/20.4.237/neoforge-20.4.237-client.jar";
    private const string UniversalPath = "net/neoforged/neoforge/20.4.237/neoforge-20.4.237-universal.jar";

    [Fact]
    public async Task ProfileReaderFindsNeoForgeOutputsAndEmbeddedRuntime()
    {
        var installerPath = Path.Combine(TempRoot, "installer.jar");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllBytesAsync(installerPath, CreateInstallerBytes());

        var artifacts = await NeoForgeProcessorArtifactService.ReadExpectedArtifactsAsync(
            installerPath,
            CancellationToken.None);

        Assert.Collection(
            artifacts,
            artifact =>
            {
                Assert.Equal(PatchedPath, artifact.RelativePath);
                Assert.Null(artifact.TrustedSha1);
            },
            artifact =>
            {
                Assert.Equal(UniversalPath, artifact.RelativePath);
                Assert.Equal(Sha1("universal"), artifact.TrustedSha1, ignoreCase: true);
            },
            artifact =>
            {
                Assert.Equal(MappingsPath, artifact.RelativePath);
                Assert.Null(artifact.TrustedSha1);
            });
    }

    [Fact]
    public async Task MissingArtifactsAreRepairedAndPersisted()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var runner = new RecordingRunner(CreateOutputs);
        var service = CreateService(runner);

        await service.EnsureLaunchArtifactsAsync(
            minecraftDirectory,
            versionJsonPath,
            versionJson,
            allowRepair: true,
            DownloadSourcePreference.Auto,
            downloadSpeedLimitMbPerSecond: 0,
            CancellationToken.None);

        Assert.Equal(1, runner.RunCount);
        Assert.Equal("mappings", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, MappingsPath)));
        Assert.Equal("patched", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, PatchedPath)));
        Assert.Equal("universal", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, UniversalPath)));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();
        var manifest = NeoForgeProcessorArtifactService.ReadManifest(saved);
        Assert.NotNull(manifest);
        Assert.Equal("20.4.237", manifest.NeoForgeVersion);
        Assert.Equal(3, manifest.Artifacts.Count);
    }

    [Fact]
    public async Task DisabledRepairRejectsMissingArtifactsWithoutRunningInstaller()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var runner = new RecordingRunner(CreateOutputs);
        var service = CreateService(runner);

        var exception = await Assert.ThrowsAsync<InstanceRepairException>(() => service.EnsureLaunchArtifactsAsync(
            minecraftDirectory,
            versionJsonPath,
            versionJson,
            allowRepair: false,
            DownloadSourcePreference.Auto,
            downloadSpeedLimitMbPerSecond: 0,
            CancellationToken.None));

        Assert.Contains("automatic repair is disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.RunCount);
    }

    [Fact]
    public async Task PersistedHashMismatchIsRepaired()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var runner = new RecordingRunner(CreateOutputs);
        var service = CreateService(runner);
        await service.EnsureLaunchArtifactsAsync(
            minecraftDirectory, versionJsonPath, versionJson, true,
            DownloadSourcePreference.Auto, 0, CancellationToken.None);
        await File.WriteAllTextAsync(GetArtifactPath(minecraftDirectory, PatchedPath), "corrupt");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();

        await service.EnsureLaunchArtifactsAsync(
            minecraftDirectory, versionJsonPath, saved, true,
            DownloadSourcePreference.Auto, 0, CancellationToken.None);

        Assert.Equal(2, runner.RunCount);
        Assert.Equal("patched", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, PatchedPath)));
    }

    [Fact]
    public async Task InstallerFailureDoesNotPublishPartialOutputs()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var runner = new RecordingRunner(directory =>
        {
            WriteArtifact(directory, MappingsPath, "mappings");
            throw new InvalidOperationException("processor failed");
        });
        var service = CreateService(runner);

        await Assert.ThrowsAsync<InstanceRepairException>(() => service.EnsureLaunchArtifactsAsync(
            minecraftDirectory, versionJsonPath, versionJson, true,
            DownloadSourcePreference.Auto, 0, CancellationToken.None));

        Assert.False(File.Exists(GetArtifactPath(minecraftDirectory, MappingsPath)));
        Assert.False(File.Exists(GetArtifactPath(minecraftDirectory, PatchedPath)));
        Assert.False(File.Exists(GetArtifactPath(minecraftDirectory, UniversalPath)));
    }

    [Fact]
    public async Task ConcurrentRepairRunsInstallerOnlyOnce()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var runner = new RecordingRunner(async directory =>
        {
            await Task.Delay(100);
            CreateOutputs(directory);
        });
        var service = CreateService(runner);
        var secondJson = JsonNode.Parse(versionJson.ToJsonString())!.AsObject();

        await Task.WhenAll(
            service.EnsureLaunchArtifactsAsync(
                minecraftDirectory, versionJsonPath, versionJson, true,
                DownloadSourcePreference.Auto, 0, CancellationToken.None),
            service.EnsureLaunchArtifactsAsync(
                minecraftDirectory, versionJsonPath, secondJson, true,
                DownloadSourcePreference.Auto, 0, CancellationToken.None));

        Assert.Equal(1, runner.RunCount);
    }

    [Fact]
    public async Task CancelledRepairCleansSessionDirectory()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new RecordingRunner(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        var service = CreateService(runner);
        using var cancellation = new CancellationTokenSource();
        var repair = service.EnsureLaunchArtifactsAsync(
            minecraftDirectory,
            versionJsonPath,
            versionJson,
            true,
            DownloadSourcePreference.Auto,
            0,
            cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repair);

        var repairRoot = Path.Combine(TempRoot, "launcher-neoforge-repair");
        Assert.False(Directory.Exists(repairRoot) && Directory.EnumerateFileSystemEntries(repairRoot).Any());
    }

    [Fact]
    public void IdentityReaderUsesNeoForgeLaunchArguments()
    {
        var json = new JsonObject
        {
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray(
                    "--fml.neoForgeVersion",
                    "26.1.2.78",
                    "--fml.mcVersion",
                    "26.1.2")
            },
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = "net.neoforged.fancymodloader:loader:11.0.15" }
            }
        };

        Assert.True(NeoForgeProcessorArtifactService.TryResolveNeoForgeIdentity(
            json,
            out var minecraftVersion,
            out var neoForgeVersion));
        Assert.Equal("26.1.2", minecraftVersion);
        Assert.Equal("26.1.2.78", neoForgeVersion);
    }

    private NeoForgeProcessorArtifactService CreateService(IForgeInstallerRunner runner) =>
        new(
            new HttpClient(new InstallerHandler(CreateInstallerBytes())),
            runner,
            new TestFinalVersionInstaller(),
            tempRootDirectory: TempRoot);

    private async Task<(string MinecraftDirectory, string VersionJsonPath, JsonObject VersionJson)> CreateVersionAsync()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "pack");
        Directory.CreateDirectory(versionDirectory);
        var json = new JsonObject
        {
            ["id"] = "pack",
            ["launcher"] = new JsonObject { ["minecraftVersion"] = "1.20.4" },
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = "net.neoforged.fancymodloader:loader:2.0.17" }
            },
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray(
                    "--fml.neoForgeVersion",
                    "20.4.237",
                    "--fml.mcVersion",
                    "1.20.4")
            }
        };
        var path = Path.Combine(versionDirectory, "pack.json");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        return (minecraftDirectory, path, json);
    }

    private static void CreateOutputs(string minecraftDirectory)
    {
        WriteArtifact(minecraftDirectory, MappingsPath, "mappings");
        WriteArtifact(minecraftDirectory, PatchedPath, "patched");
        WriteArtifact(minecraftDirectory, UniversalPath, "universal");
    }

    private static void WriteArtifact(string minecraftDirectory, string relativePath, string content)
    {
        var path = GetArtifactPath(minecraftDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string GetArtifactPath(string minecraftDirectory, string relativePath) =>
        Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] CreateInstallerBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var profileEntry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(profileEntry.Open()))
            {
                writer.Write(
                    $$"""
                    {
                      "data": {
                        "MAPPINGS": { "client": "[net.neoforged:neoform:1.20.4-build:mappings@txt]" },
                        "PATCHED": { "client": "[net.neoforged:neoforge:20.4.237:client]" }
                      },
                      "libraries": [
                        {
                          "name": "net.neoforged:neoforge:20.4.237:universal",
                          "downloads": {
                            "artifact": {
                              "path": "{{UniversalPath}}",
                              "sha1": "{{Sha1("universal")}}",
                              "size": 9
                            }
                          }
                        }
                      ],
                      "processors": [
                        { "args": ["--task", "MCP_DATA", "--output", "{MAPPINGS}"] },
                        { "args": ["--clean", "{MC_SRG}", "--output", "{PATCHED}"] },
                        { "sides": ["server"], "args": ["--output", "[example:server:1.0]"] }
                      ]
                    }
                    """);
            }

            var universalEntry = archive.CreateEntry($"maven/{UniversalPath}");
            using var universalWriter = new StreamWriter(universalEntry.Open());
            universalWriter.Write("universal");
        }
        return stream.ToArray();
    }

    private static string Sha1(string content) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private sealed class RecordingRunner : IForgeInstallerRunner
    {
        private readonly Func<string, CancellationToken, Task> callback;
        private int runCount;

        public RecordingRunner(Action<string> callback)
            : this((directory, _) =>
            {
                callback(directory);
                return Task.CompletedTask;
            })
        {
        }

        public RecordingRunner(Func<string, Task> callback)
            : this((directory, _) => callback(directory))
        {
        }

        public RecordingRunner(Func<string, CancellationToken, Task> callback)
        {
            this.callback = callback;
        }

        public int RunCount => Volatile.Read(ref runCount);

        public Task RunInstallerAsync(
            string javaCommand,
            string installerJarPath,
            string minecraftDirectory,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref runCount);
            Assert.True(Directory.Exists(Path.Combine(minecraftDirectory, "versions")));
            return callback(minecraftDirectory, cancellationToken);
        }
    }

    private sealed class TestFinalVersionInstaller : IFinalVersionInstaller
    {
        public async Task InstallAsync(
            string gameDirectory,
            string versionName,
            DownloadSourcePreference downloadSourcePreference,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            var versionDirectory = Path.Combine(gameDirectory, "versions", versionName);
            Directory.CreateDirectory(versionDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, $"{versionName}.json"),
                $$"""{"id":"{{versionName}}"}""",
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, $"{versionName}.jar"),
                "vanilla",
                cancellationToken);
        }
    }

    private sealed class InstallerHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(content)
            });
        }
    }
}
