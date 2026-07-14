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
                "com/example/processor/1.0/processor-1.0.jar",
                "com/example/profile/1.0/profile-1.0.jar"
            ],
            plan.PrerequisiteLibraries.Select(library => library.Artifact.RelativePath));
        Assert.Equal("com/example/runtime/1.0/runtime-1.0.jar", Assert.Single(plan.RuntimeLibraries).Artifact.RelativePath);
        Assert.Equal("com/example/output/1.0/output-1.0.jar", Assert.Single(plan.ProcessorOutputs).RelativePath);
        Assert.NotNull(plan.PrerequisiteLibraries.Single(library => library.Artifact.LibraryName == "com.example:profile:1.0").EmbeddedEntryName);
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

    private async Task<string> WriteInstallerAsync(bool includeEmbeddedExternal)
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"installer-{Guid.NewGuid():N}.jar");
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", $$"""
            {
              "data": { "OUTPUT": { "client": "[com.example:output:1.0]" } },
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
                  "args": ["--output", "{OUTPUT}"]
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
