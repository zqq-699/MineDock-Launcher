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
    public async Task MissingLibraryIsRecoveredFromResolvedStandardMetadata()
    {
        const string versionName = "Loader-1.18.2";
        const string relativePath = "com/example/runtime/1.0/runtime-1.0.jar";
        const string libraryContent = "runtime";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, relativePath, libraryContent, createLibrary: false);
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

    [Fact]
    public async Task PostInstallValidationDoesNotRehashFilesVerifiedByCurrentDownloadOperation()
    {
        const string versionName = "Fabric-1.20.6";
        const string relativeLibraryPath = "example/library/1.0/library-1.0.jar";
        const string clientContent = "client";
        const string libraryContent = "library";
        const string assetContent = "asset";
        const string assetHash = "05fac94380a70241f23780e7aef62b190894238f";
        const string indexContent = "{\"objects\":{\"example/asset\":{\"hash\":\"05fac94380a70241f23780e7aef62b190894238f\",\"size\":5}}}";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, relativeLibraryPath, libraryContent);
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        var clientPath = Path.Combine(versionDirectory, $"{versionName}.jar");
        await File.WriteAllTextAsync(clientPath, clientContent);
        var libraryPath = Path.Combine(minecraftDirectory, "libraries", relativeLibraryPath.Replace('/', Path.DirectorySeparatorChar));
        var indexPath = Path.Combine(minecraftDirectory, "assets", "indexes", "1.20.6.json");
        var assetPath = Path.Combine(minecraftDirectory, "assets", "objects", assetHash[..2], assetHash);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        await File.WriteAllTextAsync(indexPath, indexContent);
        await File.WriteAllTextAsync(assetPath, assetContent);
        await AddClientAndAssetMetadataAsync(versionDirectory, versionName, clientContent, indexContent, assetHash, assetContent);

        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        MarkVerified(operation, clientPath, clientContent);
        MarkVerified(operation, libraryPath, libraryContent);
        MarkVerified(operation, indexPath, indexContent);
        MarkVerified(operation, assetPath, assetContent);
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>())), downloadSpeedLimitState: null);

        using var clientLock = new FileStream(clientPath, FileMode.Open, FileAccess.Read, FileShare.None);
        using var libraryLock = new FileStream(libraryPath, FileMode.Open, FileAccess.Read, FileShare.None);
        using var assetLock = new FileStream(assetPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = await service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            operation);

        Assert.True(result.LaunchAllowed);
    }

    [Fact]
    public async Task PostInstallValidationFullyVerifiesAndRepairsUntrackedExistingFile()
    {
        const string versionName = "Vanilla";
        const string relativePath = "example/library/1.0/library-1.0.jar";
        const string expectedContent = "library";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, relativePath, expectedContent);
        var libraryPath = Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(libraryPath, "corrupt");
        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>
        {
            ["https://example.test/" + relativePath] = expectedContent
        })), downloadSpeedLimitState: null);

        var result = await service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, Path.Combine(minecraftDirectory, "versions", versionName)),
            operation);

        Assert.True(result.LaunchAllowed);
        Assert.Equal(1, result.RepairedCount);
        Assert.Equal(expectedContent, await File.ReadAllTextAsync(libraryPath));
        Assert.True(operation.IsVerified(
            libraryPath,
            DownloadIntegrityExpectation.Sha1(Sha1(expectedContent), Encoding.UTF8.GetByteCount(expectedContent))));
    }

    [Fact]
    public async Task PostInstallValidationReadsPrivateVersionFilesAndSharedGameContentFromSeparateRoots()
    {
        const string versionName = "Fabric-1.20.6";
        const string relativePath = "example/library/1.0/library-1.0.jar";
        const string libraryContent = "library";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var privateWorkspace = Path.Combine(TempRoot, "sandbox", ".minecraft");
        CreateVersion(privateWorkspace, versionName, relativePath, libraryContent, createLibrary: false);
        var sharedLibrary = Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(sharedLibrary)!);
        await File.WriteAllTextAsync(sharedLibrary, libraryContent);
        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);

        var result = await service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(
                minecraftDirectory,
                versionName,
                Path.Combine(privateWorkspace, "versions", versionName)),
            operation);

        Assert.True(result.LaunchAllowed);
        Assert.False(File.Exists(Path.Combine(minecraftDirectory, "versions", versionName, $"{versionName}.json")));
    }

    [Fact]
    public async Task ExplicitFullRepairStillRehashesPreviouslyMarkedFile()
    {
        const string versionName = "Vanilla";
        const string relativePath = "example/library/1.0/library-1.0.jar";
        const string expectedContent = "library";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, relativePath, expectedContent);
        var libraryPath = Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        MarkVerified(operation, libraryPath, expectedContent);
        await File.WriteAllTextAsync(libraryPath, "corrupt");
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>
        {
            ["https://example.test/" + relativePath] = expectedContent
        })), downloadSpeedLimitState: null);

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, Path.Combine(minecraftDirectory, "versions", versionName)),
            new GameFileRepairOptions(AllowRepair: true));

        Assert.True(result.LaunchAllowed);
        Assert.Equal(1, result.RepairedCount);
        Assert.Equal(expectedContent, await File.ReadAllTextAsync(libraryPath));
    }

    [Fact]
    public async Task PostInstallValidationRejectsUnreadableAssetIndex()
    {
        const string versionName = "Vanilla";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        var jsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        json["assetIndex"] = new JsonObject
        {
            ["id"] = "1.20.6",
            ["url"] = "https://example.test/assets/1.20.6.json",
            ["sha1"] = Sha1("valid"),
            ["size"] = 5
        };
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString());
        var indexPath = Path.Combine(minecraftDirectory, "assets", "indexes", "1.20.6.json");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        await File.WriteAllTextAsync(indexPath, "not-json");
        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>())), downloadSpeedLimitState: null);

        var result = await service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            operation);

        Assert.False(result.LaunchAllowed);
        Assert.Equal(GameFileRepairFailureReason.MetadataIncomplete, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public async Task PostInstallValidationRejectsUnparseableVersionMetadata()
    {
        const string versionName = "Vanilla";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(Path.Combine(versionDirectory, $"{versionName}.json"), "{");
        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>())), downloadSpeedLimitState: null);

        var result = await service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            operation);

        Assert.False(result.LaunchAllowed);
        Assert.Equal(GameFileRepairFailureReason.MetadataIncomplete, Assert.Single(result.Failures).Reason);
    }

    private static async Task AddClientAndAssetMetadataAsync(
        string versionDirectory,
        string versionName,
        string clientContent,
        string indexContent,
        string assetHash,
        string assetContent)
    {
        var jsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(jsonPath))!.AsObject();
        json["downloads"] = new JsonObject
        {
            ["client"] = new JsonObject
            {
                ["url"] = "https://example.test/client.jar",
                ["sha1"] = Sha1(clientContent),
                ["size"] = Encoding.UTF8.GetByteCount(clientContent)
            }
        };
        json["assetIndex"] = new JsonObject
        {
            ["id"] = "1.20.6",
            ["url"] = "https://example.test/assets/1.20.6.json",
            ["sha1"] = Sha1(indexContent),
            ["size"] = Encoding.UTF8.GetByteCount(indexContent)
        };
        await File.WriteAllTextAsync(jsonPath, json.ToJsonString());
        Assert.Equal(assetHash, Sha1(assetContent));
    }

    private static void MarkVerified(MinecraftDownloadOperationContext operation, string path, string content)
    {
        operation.MarkVerified(
            path,
            DownloadIntegrityExpectation.Sha1(Sha1(content), Encoding.UTF8.GetByteCount(content)));
    }

    private static void CreateVersion(string minecraftDirectory, string versionName, string relativePath, string libraryContent, bool createLibrary = true)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, $"{versionName}.jar"), "client");
        var libraryPath = Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        if (createLibrary)
            File.WriteAllText(libraryPath, libraryContent);
        var json = new JsonObject
        {
            ["id"] = versionName,
            ["libraries"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "com.example:runtime:1.0",
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
