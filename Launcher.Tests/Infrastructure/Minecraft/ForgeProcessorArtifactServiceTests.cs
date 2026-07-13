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

public sealed class ForgeProcessorArtifactServiceTests : TestTempDirectory
{
    private const string ExtraPath = "net/minecraft/client/1.20.1-build/client-1.20.1-build-extra.jar";
    private const string SrgPath = "net/minecraft/client/1.20.1-build/client-1.20.1-build-srg.jar";
    private const string PatchedPath = "net/minecraftforge/forge/1.20.1-47.4.20/forge-1.20.1-47.4.20-client.jar";
    private const string RuntimePath = "net/minecraftforge/fmlcore/1.20.1-47.4.20/fmlcore-1.20.1-47.4.20.jar";

    [Fact]
    public async Task ProfileReaderFindsClientOutputsAndTrustedHashes()
    {
        var installerPath = Path.Combine(TempRoot, "installer.jar");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllBytesAsync(installerPath, CreateInstallerBytes());

        var artifacts = await ForgeProcessorArtifactService.ReadExpectedArtifactsAsync(
            installerPath,
            CancellationToken.None);

        Assert.Collection(
            artifacts,
            artifact =>
            {
                Assert.Equal(ExtraPath, artifact.RelativePath);
                Assert.Equal(Sha1("extra"), artifact.TrustedSha1, ignoreCase: true);
            },
            artifact =>
            {
                Assert.Equal(SrgPath, artifact.RelativePath);
                Assert.Null(artifact.TrustedSha1);
            },
            artifact =>
            {
                Assert.Equal(RuntimePath, artifact.RelativePath);
                Assert.Equal(Sha1("runtime"), artifact.TrustedSha1, ignoreCase: true);
            },
            artifact =>
            {
                Assert.Equal(PatchedPath, artifact.RelativePath);
                Assert.Equal(Sha1("patched"), artifact.TrustedSha1, ignoreCase: true);
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
        Assert.Equal("extra", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, ExtraPath)));
        Assert.Equal("srg", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, SrgPath)));
        Assert.Equal("patched", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, PatchedPath)));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();
        var manifest = ForgeProcessorArtifactService.ReadManifest(saved);
        Assert.NotNull(manifest);
        Assert.Equal(4, manifest.Artifacts.Count);
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
        await File.WriteAllTextAsync(GetArtifactPath(minecraftDirectory, SrgPath), "corrupt");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();

        await service.EnsureLaunchArtifactsAsync(
            minecraftDirectory, versionJsonPath, saved, true,
            DownloadSourcePreference.Auto, 0, CancellationToken.None);

        Assert.Equal(2, runner.RunCount);
        Assert.Equal("srg", await File.ReadAllTextAsync(GetArtifactPath(minecraftDirectory, SrgPath)));
    }

    [Fact]
    public async Task LegacyProcessorOnlyManifestIsMigratedToRuntimeCompleteManifest()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var launcher = versionJson["launcher"]!.AsObject();
        launcher["forgeProcessorArtifacts"] = new JsonObject
        {
            ["minecraftVersion"] = "1.20.1",
            ["forgeVersion"] = "47.4.20",
            ["artifacts"] = new JsonArray()
        };
        await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString());
        var runner = new RecordingRunner(CreateOutputs);
        var service = CreateService(runner);

        await service.EnsureLaunchArtifactsAsync(
            minecraftDirectory, versionJsonPath, versionJson, true,
            DownloadSourcePreference.Auto, 0, CancellationToken.None);

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();
        Assert.Equal(2, saved["launcher"]?["forgeProcessorArtifacts"]?["schemaVersion"]?.GetValue<int>());
        Assert.Equal(4, ForgeProcessorArtifactService.ReadManifest(saved)?.Artifacts.Count);
        Assert.True(File.Exists(GetArtifactPath(minecraftDirectory, RuntimePath)));
    }

    [Fact]
    public async Task InstallerFailureDoesNotPublishPartialProcessorOutputs()
    {
        var (minecraftDirectory, versionJsonPath, versionJson) = await CreateVersionAsync();
        var runner = new RecordingRunner(directory =>
        {
            WriteArtifact(directory, ExtraPath, "extra");
            throw new InvalidOperationException("processor failed");
        });
        var service = CreateService(runner);

        await Assert.ThrowsAsync<InstanceRepairException>(() => service.EnsureLaunchArtifactsAsync(
            minecraftDirectory, versionJsonPath, versionJson, true,
            DownloadSourcePreference.Auto, 0, CancellationToken.None));

        Assert.False(File.Exists(GetArtifactPath(minecraftDirectory, ExtraPath)));
        Assert.False(File.Exists(GetArtifactPath(minecraftDirectory, SrgPath)));
        Assert.False(File.Exists(GetArtifactPath(minecraftDirectory, PatchedPath)));
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

        var repairRoot = Path.Combine(TempRoot, "launcher-forge-repair");
        Assert.False(Directory.Exists(repairRoot) && Directory.EnumerateFileSystemEntries(repairRoot).Any());
    }

    private ForgeProcessorArtifactService CreateService(IForgeInstallerRunner runner) =>
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
            ["launcher"] = new JsonObject { ["minecraftVersion"] = "1.20.1" },
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = "net.minecraftforge:fmlloader:1.20.1-47.4.20" }
            },
            ["arguments"] = new JsonObject
            {
                ["game"] = new JsonArray("--fml.forgeVersion", "47.4.20")
            }
        };
        var path = Path.Combine(versionDirectory, "pack.json");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        return (minecraftDirectory, path, json);
    }

    private static void CreateOutputs(string minecraftDirectory)
    {
        WriteArtifact(minecraftDirectory, ExtraPath, "extra");
        WriteArtifact(minecraftDirectory, SrgPath, "srg");
        WriteArtifact(minecraftDirectory, PatchedPath, "patched");
        WriteArtifact(minecraftDirectory, RuntimePath, "runtime");
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
            var entry = archive.CreateEntry("install_profile.json");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write(
                    $$"""
                {
                  "data": {
                    "EXTRA": { "client": "[net.minecraft:client:1.20.1-build:extra]" },
                    "EXTRA_SHA": { "client": "'{{Sha1("extra")}}'" },
                    "SRG": { "client": "[net.minecraft:client:1.20.1-build:srg]" },
                    "PATCHED": { "client": "[net.minecraftforge:forge:1.20.1-47.4.20:client]" },
                    "PATCHED_SHA": { "client": "'{{Sha1("patched")}}'" }
                  },
                  "libraries": [
                    {
                      "name": "net.minecraftforge:fmlcore:1.20.1-47.4.20",
                      "downloads": {
                        "artifact": {
                          "path": "{{RuntimePath}}",
                          "sha1": "{{Sha1("runtime")}}",
                          "size": 7
                        }
                      }
                    }
                  ],
                  "processors": [
                    { "sides": ["client"], "args": ["--extra", "{EXTRA}"], "outputs": { "{EXTRA}": "{EXTRA_SHA}" } },
                    { "args": ["--output", "{SRG}"] },
                    { "args": ["--output", "{PATCHED}"], "outputs": { "{PATCHED}": "{PATCHED_SHA}" } },
                    { "sides": ["server"], "args": ["--output", "[example:server:1.0]"] }
                  ]
                }
                """);
            }
            var runtimeEntry = archive.CreateEntry($"maven/{RuntimePath}");
            using var runtimeWriter = new StreamWriter(runtimeEntry.Open());
            runtimeWriter.Write("runtime");
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
