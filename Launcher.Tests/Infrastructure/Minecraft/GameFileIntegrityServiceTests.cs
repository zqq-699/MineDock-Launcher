/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class GameFileIntegrityServiceTests : TestTempDirectory
{
    [Fact]
    public async Task MissingClasspathLibraryIsRecoveredFromResolvedMetadataWithoutArtifactNameSpecialCase()
    {
        const string versionName = "Forge-1.18.2";
        const string relativePath = "net/minecraftforge/fmlcore/1.18.2-40.2.17/fmlcore-1.18.2-40.2.17.jar";
        const string libraryContent = "fmlcore";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, relativePath, libraryContent);
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>
        {
            ["https://example.test/" + relativePath] = libraryContent
        })), downloadSpeedLimitState: null);

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, Path.Combine(minecraftDirectory, "versions", versionName)),
            new GameFileRepairOptions(AllowRepair: true));

        Assert.True(result.LaunchAllowed);
        Assert.Equal(1, result.RepairedCount);
        Assert.Equal(libraryContent, await File.ReadAllTextAsync(Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task PersistedProcessorOutputParticipatesInManifestWhenAutomaticRepairIsDisabled()
    {
        const string versionName = "Forge-1.20.1";
        const string relativePath = "net/minecraftforge/fmlloader/1.20.1-47.4.20/fmlloader-1.20.1-47.4.20.jar";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var jsonPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        json["launcher"] = new JsonObject
        {
            ["forgeProcessorArtifacts"] = new JsonObject
            {
                ["schemaVersion"] = 2,
                ["minecraftVersion"] = "1.20.1",
                ["forgeVersion"] = "47.4.20",
                ["artifacts"] = new JsonArray
                {
                    new JsonObject { ["path"] = relativePath, ["sha1"] = Sha1("processor") }
                }
            }
        };
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString());
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, Path.Combine(minecraftDirectory, "versions", versionName)),
            new GameFileRepairOptions(AllowRepair: false));

        var failure = Assert.Single(result.Failures);
        Assert.False(result.LaunchAllowed);
        Assert.Equal(relativePath, Path.GetRelativePath(Path.Combine(minecraftDirectory, "libraries"), failure.TargetPath).Replace('\\', '/'));
        Assert.Equal("ProcessorRegeneration", failure.RecoveryMethod);
    }

    [Fact]
    public async Task FinalCommandWithMissingClasspathPathIsBlockedBeforeProcessStart()
    {
        const string versionName = "Vanilla";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);
        var missingPath = Path.Combine(minecraftDirectory, "libraries", "missing.jar");
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(missingPath);

        var result = await service.ValidateFinalLaunchCommandAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, Path.Combine(minecraftDirectory, "versions", versionName)),
            startInfo);

        var failure = Assert.Single(result.Failures);
        Assert.False(result.LaunchAllowed);
        Assert.Equal(GameFileRepairFailureReason.FinalLaunchPlanInvalid, failure.Reason);
        Assert.Equal(missingPath, failure.TargetPath);
    }

    [Fact]
    public async Task DottedAssetIndexIdentifierIsAcceptedAndRepaired()
    {
        const string versionName = "Vanilla";
        const string indexContent = "{\"objects\":{}}";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var jsonPath = Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        json["assetIndex"] = new JsonObject
        {
            ["id"] = "1.18",
            ["url"] = "https://example.test/assets/1.18.json",
            ["sha1"] = Sha1(indexContent),
            ["size"] = Encoding.UTF8.GetByteCount(indexContent)
        };
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString());
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>
        {
            ["https://example.test/assets/1.18.json"] = indexContent
        })), downloadSpeedLimitState: null);

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, Path.Combine(minecraftDirectory, "versions", versionName)),
            new GameFileRepairOptions(AllowRepair: true));

        Assert.True(result.LaunchAllowed);
        Assert.True(File.Exists(Path.Combine(minecraftDirectory, "assets", "indexes", "1.18.json")));
    }

    private static void CreateVersion(string minecraftDirectory, string versionName, string relativePath, string libraryContent)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, $"{versionName}.jar"), "client");
        var libraryPath = Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        if (!relativePath.Contains("fmlcore", StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(libraryPath, libraryContent);
        var json = new JsonObject
        {
            ["id"] = versionName,
            ["libraries"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "net.minecraftforge:fmlcore:1.18.2-40.2.17",
                    ["downloads"] = new JsonObject
                    {
                        ["artifact"] = new JsonObject
                        {
                            ["path"] = relativePath,
                            ["url"] = "https://example.test/" + relativePath,
                            ["sha1"] = Sha1(libraryContent),
                            ["size"] = Encoding.UTF8.GetByteCount(libraryContent)
                        }
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(versionDirectory, $"{versionName}.json"), json.ToJsonString());
    }

    private static string Sha1(string value) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class ContentHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null && responses.TryGetValue(request.RequestUri.AbsoluteUri, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
