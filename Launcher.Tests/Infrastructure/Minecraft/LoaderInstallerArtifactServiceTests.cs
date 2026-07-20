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

public sealed class LoaderInstallerArtifactServiceTests : TestTempDirectory
{
    [Fact]
    public async Task PlanIncludesProfileProcessorAndVersionRuntimeLibraries()
    {
        var installerPath = await WriteInstallerAsync(includeEmbeddedExternal: true);
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));

        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);

        Assert.Equal(
            [
                "com/example/classpath/1.0/classpath-1.0.jar",
                "com/example/input/1.0/input-1.0.jar",
                "com/example/processor/1.0/processor-1.0.jar",
                "com/example/profile/1.0/profile-1.0.jar"
            ],
            plan.PrerequisiteLibraries.Select(library => library.Artifact.RelativePath));
        Assert.Equal("com/example/runtime/1.0/runtime-1.0.jar", Assert.Single(plan.RuntimeLibraries).Artifact.RelativePath);
        Assert.Equal("com/example/output/1.0/output-1.0.jar", Assert.Single(plan.ProcessorOutputs).RelativePath);
        Assert.NotNull(plan.PrerequisiteLibraries.Single(library => library.Artifact.LibraryName == "com.example:profile:1.0").EmbeddedEntryName);
    }

    [Fact]
    public async Task ServerPlanUsesOnlyServerAndUnsidedProcessors()
    {
        var installerPath = await WriteSideAwareInstallerAsync();
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));

        var plan = await service.ReadPlanAsync(
            installerPath,
            ModpackInstallEnvironment.Server,
            CancellationToken.None);

        Assert.Equal(
            [
                "com/example/common-processor/1.0/common-processor-1.0.jar",
                "com/example/server-input/1.0/server-input-1.0.jar",
                "com/example/server-processor/1.0/server-processor-1.0.jar"
            ],
            plan.PrerequisiteLibraries.Select(library => library.Artifact.RelativePath));
        Assert.Equal(
            "com/example/server-output/1.0/server-output-1.0.jar",
            Assert.Single(plan.ProcessorOutputs).RelativePath);
        Assert.DoesNotContain(
            plan.PrerequisiteLibraries,
            library => library.Artifact.RelativePath.Contains("client", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeclaredNonEmbeddedLibraryIsDownloaded()
    {
        var installerPath = await WriteInstallerAsync(includeEmbeddedExternal: false);
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));
        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);

        await service.MaterializePrerequisitesAsync(
            installerPath,
            plan,
            minecraftDirectory,
            DownloadSourcePreference.Official,
            0,
            CancellationToken.None);

        var path = Path.Combine(minecraftDirectory, "libraries", "com", "example", "profile", "1.0", "profile-1.0.jar");
        Assert.Equal("external", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RuntimeLibrariesAreMergedIntoStandardVersionJsonAndLegacyMetadataIsRemoved()
    {
        var installerPath = await WriteInstallerAsync(includeEmbeddedExternal: true);
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));
        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);
        var versionPath = Path.Combine(TempRoot, "pack.json");
        await File.WriteAllTextAsync(versionPath, """
            {
              "id": "pack",
              "launcher": {
                "minecraftVersion": "1.20.1",
                "forgeProcessorArtifacts": { "schemaVersion": 2 }
              },
              "libraries": [ { "name": "com.example:existing:1.0" } ]
            }
            """);

        await LoaderInstallerArtifactService.ApplyRuntimeLibrariesAsync(
            versionPath,
            plan,
            "forgeProcessorArtifacts",
            CancellationToken.None);

        var version = JsonNode.Parse(await File.ReadAllTextAsync(versionPath))!.AsObject();
        Assert.False(version["launcher"]!.AsObject().ContainsKey("forgeProcessorArtifacts"));
        Assert.Equal(
            ["com.example:runtime:1.0", "com.example:existing:1.0"],
            version["libraries"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ManifestStoreCapturesCompleteInstallerClosureAndRejectsDifferentIdentity()
    {
        var installerPath = await WriteInstallerAsync(includeEmbeddedExternal: true);
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Arbitrary Pack");
        Directory.CreateDirectory(versionDirectory);
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));
        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);
        await service.MaterializePrerequisitesAsync(
            installerPath,
            plan,
            minecraftDirectory,
            DownloadSourcePreference.Official,
            0,
            CancellationToken.None);
        await service.MaterializeRuntimeLibrariesAsync(
            installerPath,
            plan,
            minecraftDirectory,
            DownloadSourcePreference.Official,
            0,
            CancellationToken.None);
        var output = Assert.Single(plan.ProcessorOutputs);
        var outputPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "generated");
        var identity = new GameFileLoaderIdentity(LoaderKind.Forge, "9.9.9", "77.0.1");

        await LoaderArtifactManifestStore.WriteAsync(
            versionDirectory,
            minecraftDirectory,
            identity,
            installerPath,
            plan,
            CancellationToken.None);

        var result = await LoaderArtifactManifestStore.ReadAsync(
            versionDirectory,
            identity,
            CancellationToken.None);
        Assert.True(result.IsValid);
        Assert.Equal(6, result.Manifest!.Artifacts.Count);
        Assert.Contains(result.Manifest.Artifacts, artifact => artifact.Kind == LoaderArtifactKind.InstallerPrerequisite);
        Assert.Contains(result.Manifest.Artifacts, artifact => artifact.Kind == LoaderArtifactKind.RuntimeLibrary);
        Assert.Contains(result.Manifest.Artifacts, artifact => artifact.Kind == LoaderArtifactKind.ProcessorOutput);
        Assert.All(result.Manifest.Artifacts, artifact => Assert.True(MinecraftFileIntegrity.IsSha1(artifact.Sha1)));
        Assert.All(result.Manifest.Artifacts, artifact => Assert.Equal(64, artifact.Sha256.Length));

        var mismatched = await LoaderArtifactManifestStore.ReadAsync(
            versionDirectory,
            identity with { LoaderVersion = "77.0.2" },
            CancellationToken.None);
        Assert.False(mismatched.IsValid);
        Assert.Contains("identity", mismatched.Error, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> WriteInstallerAsync(bool includeEmbeddedExternal)
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"installer-{Guid.NewGuid():N}.jar");
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", $$"""
            {
              "data": {
                "INPUT": { "client": "[com.example:input:1.0]" },
                "OUTPUT": { "client": "[com.example:output:1.0]" }
              },
              "libraries": [
                {
                  "name": "com.example:profile:1.0",
                  "url": "https://example.test/",
                  "downloads": { "artifact": { "path": "com/example/profile/1.0/profile-1.0.jar", "sha1": "{{Sha1("external")}}", "size": 8 } }
                }
              ],
              "processors": [
                {
                  "sides": ["client"],
                  "jar": "com.example:processor:1.0",
                  "classpath": ["com.example:classpath:1.0"],
                  "args": ["--input", "{INPUT}", "--output", "{OUTPUT}"]
                },
                {
                  "sides": ["client"],
                  "jar": "com.example:processor:1.0",
                  "args": ["--consume-generated", "{OUTPUT}"]
                }
              ]
            }
            """);
        WriteEntry(archive, "version.json", """
            {
              "libraries": [
                { "name": "com.example:runtime:1.0", "url": "https://example.test/" }
              ]
            }
            """);
        if (includeEmbeddedExternal)
            WriteEntry(archive, "maven/com/example/profile/1.0/profile-1.0.jar", "external");
        return installerPath;
    }

    private async Task<string> WriteSideAwareInstallerAsync()
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"side-aware-installer-{Guid.NewGuid():N}.jar");
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", """
            {
              "data": {
                "INPUT": {
                  "client": "[com.example:client-input:1.0]",
                  "server": "[com.example:server-input:1.0]"
                },
                "OUTPUT": {
                  "client": "[com.example:client-output:1.0]",
                  "server": "[com.example:server-output:1.0]"
                },
                "ROOT": { "client": "client-root", "server": "server-root" }
              },
              "processors": [
                {
                  "sides": ["client"],
                  "jar": "com.example:client-processor:1.0",
                  "args": ["--input", "{INPUT}", "--output", "{OUTPUT}"]
                },
                {
                  "sides": ["server"],
                  "jar": "com.example:server-processor:1.0",
                  "args": ["--input", "{INPUT}", "--output", "{OUTPUT}", "--output", "{ROOT}/libraries/"]
                },
                {
                  "jar": "com.example:common-processor:1.0",
                  "args": []
                }
              ]
            }
            """);
        WriteEntry(archive, "version.json", """{ "libraries": [] }""");
        return installerPath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string Sha1(string value) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class TestHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            });
    }
}
