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
        var progressReports = new List<LauncherProgress>();

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, Path.Combine(minecraftDirectory, "versions", versionName)),
            new GameFileRepairOptions(AllowRepair: true),
            new InlineProgress(progressReports));

        Assert.True(result.LaunchAllowed);
        Assert.Equal(1, result.RepairedCount);
        Assert.Equal(libraryContent, await File.ReadAllTextAsync(Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Contains(
            progressReports,
            report => report.Stage == LaunchProgressStages.RevalidatingFiles && report.Percent == 84);
        Assert.Contains(
            progressReports,
            report => report.Stage == LaunchProgressStages.RevalidatingFiles && report.Percent == 90);
        Assert.Equal(
            progressReports.Where(report => report.Percent is not null).Select(report => report.Percent!.Value).Order(),
            progressReports.Where(report => report.Percent is not null).Select(report => report.Percent!.Value));
    }

    [Fact]
    public async Task CompleteVersionSkipsRepairRangesAndFinishesInitialValidationAtNinety()
    {
        const string versionName = "Complete Vanilla";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var reports = new List<LauncherProgress>();
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(
                minecraftDirectory,
                versionName,
                Path.Combine(minecraftDirectory, "versions", versionName)),
            new GameFileRepairOptions(AllowRepair: true),
            new InlineProgress(reports));

        Assert.True(result.LaunchAllowed);
        Assert.Equal([4d, 12d, 90d], reports.Select(report => report.Percent!.Value));
        Assert.All(reports, report => Assert.Equal(LaunchProgressStages.CheckingFiles, report.Stage));
    }

    [Fact]
    public async Task ValidationCancellationIsPropagated()
    {
        const string versionName = "Canceled Validation";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            new GameFileRepairOptions(AllowRepair: true),
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task PostInstallValidationCancellationIsPropagated()
    {
        const string versionName = "Canceled Post Install Validation";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            operation,
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task MissingVersionMetadataIsRebuiltFromExplicitLoaderIdentity()
    {
        const string versionName = "Recovered Vanilla";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var provider = new RecordingLoaderProvider(LoaderKind.Vanilla);
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null,
            logger: null,
            loaderProviders: [provider],
            gameInstallCoordinator: new GameInstallCoordinator());

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory)
            {
                LoaderIdentity = new GameFileLoaderIdentity(
                    LoaderKind.Vanilla,
                    "1.21.8",
                    LoaderVersion: null)
            },
            new GameFileRepairOptions(AllowRepair: true));

        Assert.True(result.LaunchAllowed);
        Assert.Equal(1, provider.InstallCount);
        Assert.True(File.Exists(Path.Combine(versionDirectory, $"{versionName}.json")));
        Assert.True(File.Exists(Path.Combine(versionDirectory, $"{versionName}.jar")));
    }

    [Fact]
    public async Task NestedLoaderDownloadFailureIsNotMisreportedAsCorruption()
    {
        const string versionName = "Forge Download Failure";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var provider = new FailingLoaderProvider(
            LoaderKind.Forge,
            new InstanceRepairException(
                "Forge sandbox repair failed.",
                new DownloadAttemptException(
                    DownloadFailureDisposition.SwitchSource,
                    DownloadFailureReason.HttpStatus,
                    "The source returned HTTP 404.",
                    statusCode: HttpStatusCode.NotFound)));
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null,
            logger: null,
            loaderProviders: [provider],
            gameInstallCoordinator: new GameInstallCoordinator());

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory)
            {
                LoaderIdentity = new GameFileLoaderIdentity(LoaderKind.Forge, "1.20.1", "47.4.20")
            },
            new GameFileRepairOptions(AllowRepair: true));

        Assert.False(result.LaunchAllowed);
        Assert.Equal(0, result.CorruptedCount);
        Assert.Equal(GameFileRepairFailureReason.DownloadFailed, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public async Task DisabledAutoRepairReportsLoaderArtifactWithoutRunningProviderOrPublishingFiles()
    {
        const string versionName = "Forge Check Only";
        const string loaderRelativePath = "org/example/generated/3.7/generated-3.7-client.jar";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        var loaderPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            loaderRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(loaderPath)!);
        await File.WriteAllTextAsync(loaderPath, "generated");
        var installerPath = Path.Combine(TempRoot, "arbitrary-installer.jar");
        await File.WriteAllTextAsync(installerPath, "installer");
        var identity = new GameFileLoaderIdentity(LoaderKind.Forge, "9.9.9", "3.7");
        await LoaderArtifactManifestStore.WriteAsync(
            versionDirectory,
            minecraftDirectory,
            identity,
            installerPath,
            new ForgeInstallerPlan(
                PrerequisiteLibraries: [],
                RuntimeLibraries: [],
                ProcessorOutputs: [new ForgeProcessorOutput(loaderRelativePath, TrustedSha1: null)],
                RuntimeLibraryMetadata: new JsonArray()),
            CancellationToken.None);
        File.Delete(loaderPath);
        var provider = new RecordingLoaderProvider(LoaderKind.Forge);
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null,
            logger: null,
            loaderProviders: [provider],
            gameInstallCoordinator: new GameInstallCoordinator());
        var progressReports = new List<LauncherProgress>();

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory)
            {
                LoaderIdentity = identity
            },
            new GameFileRepairOptions(AllowRepair: false),
            new InlineProgress(progressReports));

        Assert.False(result.LaunchAllowed);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(0, provider.InstallCount);
        Assert.False(File.Exists(loaderPath));
        Assert.True(File.Exists(LoaderArtifactManifestStore.GetPath(versionDirectory)));
        Assert.Equal(12, progressReports.Max(report => report.Percent));
        Assert.DoesNotContain(
            progressReports,
            report => report.Stage.StartsWith("Install.", StringComparison.Ordinal));

        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(loaderPath);
        var finalValidation = await service.ValidateFinalLaunchCommandAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory)
            {
                LoaderIdentity = identity
            },
            startInfo);
        Assert.False(finalValidation.LaunchAllowed);
        Assert.Contains(
            finalValidation.Failures,
            failure => failure.Category == "Classpath"
                && failure.Reason == GameFileRepairFailureReason.FinalLaunchPlanInvalid
                && failure.TargetPath == loaderPath);
    }

    [Fact]
    public async Task LoaderPublicationRollbackRestoresReplacedFilesAndRemovesNewFiles()
    {
        var existingPath = Path.Combine(TempRoot, "libraries", "existing.jar");
        var newPath = Path.Combine(TempRoot, "libraries", "new.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        await File.WriteAllTextAsync(existingPath, "old");
        var rollback = new LoaderArtifactRepairCoordinator.LoaderPublicationRollback(
            Path.Combine(TempRoot, "rollback"));

        rollback.Prepare(existingPath);
        rollback.Prepare(newPath);
        await File.WriteAllTextAsync(existingPath, "replacement");
        await File.WriteAllTextAsync(newPath, "created");
        rollback.Rollback();
        rollback.Cleanup();

        Assert.Equal("old", await File.ReadAllTextAsync(existingPath));
        Assert.False(File.Exists(newPath));
        Assert.False(Directory.Exists(Path.Combine(TempRoot, "rollback")));
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
    public async Task FinalCommandValidationDoesNotHashKnownClasspathFile()
    {
        const string versionName = "Vanilla";
        const string relativePath = "example/library/1.0/library-1.0.jar";
        const string expectedContent = "library";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        CreateVersion(minecraftDirectory, versionName, relativePath, expectedContent);
        var libraryPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(libraryPath, "corrupt");
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(libraryPath);
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);
        var request = new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory);

        var finalValidation = await service.ValidateFinalLaunchCommandAsync(request, startInfo);
        var fullValidation = await service.ValidateAndRepairAsync(
            request,
            new GameFileRepairOptions(AllowRepair: false));

        Assert.True(finalValidation.LaunchAllowed);
        Assert.False(fullValidation.LaunchAllowed);
        Assert.Contains(
            fullValidation.Failures,
            failure => failure.Category == "Library"
                && failure.Reason == GameFileRepairFailureReason.Corrupted);
    }

    [Fact]
    public async Task FinalCommandValidationDoesNotReadAssetIndex()
    {
        const string versionName = "Vanilla";
        const string relativePath = "example/library/1.0/library-1.0.jar";
        const string libraryContent = "library";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        CreateVersion(minecraftDirectory, versionName, relativePath, libraryContent);
        var versionJsonPath = Path.Combine(versionDirectory, $"{versionName}.json");
        var versionJson = JsonNode.Parse(await File.ReadAllTextAsync(versionJsonPath))!.AsObject();
        versionJson["assetIndex"] = new JsonObject
        {
            ["id"] = "broken",
            ["url"] = "https://example.test/assets/broken.json",
            ["sha1"] = Sha1("{"),
            ["size"] = 1
        };
        await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString());
        var indexesDirectory = Path.Combine(minecraftDirectory, "assets", "indexes");
        Directory.CreateDirectory(indexesDirectory);
        await File.WriteAllTextAsync(Path.Combine(indexesDirectory, "broken.json"), "{");
        var libraryPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(libraryPath);
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);
        var request = new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory);

        var finalValidation = await service.ValidateFinalLaunchCommandAsync(request, startInfo);
        var fullValidation = await service.ValidateAndRepairAsync(
            request,
            new GameFileRepairOptions(AllowRepair: false));

        Assert.True(finalValidation.LaunchAllowed);
        Assert.False(fullValidation.LaunchAllowed);
        Assert.Equal(
            GameFileRepairFailureReason.MetadataIncomplete,
            Assert.Single(fullValidation.Failures).Reason);
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
    public async Task PrelaunchValidationAcceptsSameSizedAssetObjectWithoutHashing()
    {
        const string versionName = "Vanilla";
        const string expectedAssetContent = "asset";
        const string sameSizeWrongAsset = "wrong";
        var assetHash = Sha1(expectedAssetContent);
        var indexContent = new JsonObject
        {
            ["objects"] = new JsonObject
            {
                ["example/asset"] = new JsonObject
                {
                    ["hash"] = assetHash,
                    ["size"] = Encoding.UTF8.GetByteCount(expectedAssetContent)
                }
            }
        }.ToJsonString();
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        var indexPath = Path.Combine(minecraftDirectory, "assets", "indexes", "1.20.6.json");
        var assetPath = Path.Combine(minecraftDirectory, "assets", "objects", assetHash[..2], assetHash);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        await File.WriteAllTextAsync(indexPath, indexContent);
        await File.WriteAllTextAsync(assetPath, sameSizeWrongAsset);
        await AddClientAndAssetMetadataAsync(
            versionDirectory,
            versionName,
            "client",
            indexContent,
            assetHash,
            expectedAssetContent);
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);

        var result = await service.ValidateAndRepairAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            new GameFileRepairOptions(AllowRepair: false));

        Assert.True(result.LaunchAllowed);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(sameSizeWrongAsset, await File.ReadAllTextAsync(assetPath));
    }

    [Fact]
    public async Task PostInstallValidationReusesUnchangedFileSnapshots()
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

        Assert.True(operation.IsVerified(
            libraryPath,
            DownloadIntegrityExpectation.Sha1(Sha1(libraryContent), Encoding.UTF8.GetByteCount(libraryContent))));
        var result = await service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            operation);

        Assert.True(result.LaunchAllowed);
    }

    [Fact]
    public async Task PostInstallValidationRehashesSameSizeReplacementWithRestoredWriteTime()
    {
        const string versionName = "Snapshot Replacement";
        const string relativePath = "example/library/1.0/library-1.0.jar";
        const string expectedContent = "library";
        const string corruptContent = "corrupt";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        CreateVersion(minecraftDirectory, versionName, relativePath, expectedContent);
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        var libraryPath = Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var originalWriteTime = File.GetLastWriteTimeUtc(libraryPath);
        using var operation = new MinecraftDownloadOperationContext(minecraftDirectory);
        MarkVerified(operation, libraryPath, expectedContent);
        var replacementPath = Path.Combine(TempRoot, "replacement.jar");
        await File.WriteAllTextAsync(replacementPath, corruptContent);
        File.SetLastWriteTimeUtc(replacementPath, originalWriteTime);
        File.Move(replacementPath, libraryPath, overwrite: true);
        File.SetLastWriteTimeUtc(libraryPath, originalWriteTime);
        var service = new GameFileIntegrityService(new HttpClient(new ContentHandler(new Dictionary<string, string>
        {
            ["https://example.test/" + relativePath] = expectedContent
        })), downloadSpeedLimitState: null);

        var result = await service.ValidateInstalledVersionAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            operation);

        Assert.True(result.LaunchAllowed);
        Assert.Equal(1, result.RepairedCount);
        Assert.Equal(expectedContent, await File.ReadAllTextAsync(libraryPath));
    }

    [Fact]
    public async Task VerificationSnapshotRejectsInPlaceMutationWithRestoredWriteTime()
    {
        const string expectedContent = "library";
        const string corruptContent = "corrupt";
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "in-place.jar");
        await File.WriteAllTextAsync(path, expectedContent);
        var writeTime = File.GetLastWriteTimeUtc(path);
        using var operation = new MinecraftDownloadOperationContext(TempRoot);
        var expectation = DownloadIntegrityExpectation.Sha1(
            Sha1(expectedContent),
            Encoding.UTF8.GetByteCount(expectedContent));
        operation.MarkVerified(path, expectation);

        await File.WriteAllTextAsync(path, corruptContent);
        File.SetLastWriteTimeUtc(path, writeTime);

        Assert.False(operation.IsVerified(path, expectation));
    }

    [Fact]
    public async Task VerifiedFileLeaseBlocksConcurrentWrites()
    {
        const string content = "library";
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "leased.jar");
        await File.WriteAllTextAsync(path, content);
        using var operation = new MinecraftDownloadOperationContext(TempRoot);
        var expectation = DownloadIntegrityExpectation.Sha1(Sha1(content), Encoding.UTF8.GetByteCount(content));
        operation.MarkVerified(path, expectation);

        using var lease = operation.AcquireVerifiedFileLease(path, expectation);

        Assert.NotNull(lease);
        Assert.ThrowsAny<IOException>(() => File.WriteAllText(path, "corrupt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitlyAllowedUserAgentIsAcceptedInsideOrOutsideMinecraftDirectory(bool insideMinecraftDirectory)
    {
        const string versionName = "Custom Agent";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var agentPath = insideMinecraftDirectory
            ? Path.Combine(versionDirectory, "custom-agent.jar")
            : Path.Combine(TempRoot, "external-agent.jar");
        await File.WriteAllTextAsync(agentPath, "agent");
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        startInfo.ArgumentList.Add($"-javaagent:{agentPath}=option");
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);

        var result = await service.ValidateFinalLaunchCommandAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory)
            {
                AllowedAdditionalCommandFilePaths = [agentPath]
            },
            startInfo);

        Assert.True(result.LaunchAllowed);
    }

    [Fact]
    public async Task UnapprovedAgentIsRejectedEvenWhenItExists()
    {
        const string versionName = "Unapproved Agent";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var agentPath = Path.Combine(TempRoot, "unapproved-agent.jar");
        await File.WriteAllTextAsync(agentPath, "agent");
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        startInfo.ArgumentList.Add($"-javaagent:{agentPath}");
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);

        var result = await service.ValidateFinalLaunchCommandAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory),
            startInfo);

        var failure = Assert.Single(result.Failures.Where(item => item.Category == "JavaAgent"));
        Assert.False(result.LaunchAllowed);
        Assert.Contains("explicitly allowed", failure.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowedAgentReparsePointIsRejectedWhenSupported()
    {
        const string versionName = "Linked Agent";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        CreateVersion(minecraftDirectory, versionName, "example/library/1.0/library-1.0.jar", "library");
        var targetPath = Path.Combine(TempRoot, "agent-target.jar");
        var linkPath = Path.Combine(TempRoot, "agent-link.jar");
        await File.WriteAllTextAsync(targetPath, "agent");
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }
        var startInfo = new ProcessStartInfo { UseShellExecute = false };
        startInfo.ArgumentList.Add($"-javaagent:{linkPath}");
        var service = new GameFileIntegrityService(
            new HttpClient(new ContentHandler(new Dictionary<string, string>())),
            downloadSpeedLimitState: null);

        var result = await service.ValidateFinalLaunchCommandAsync(
            new GameFileIntegrityRequest(minecraftDirectory, versionName, versionDirectory)
            {
                AllowedAdditionalCommandFilePaths = [linkPath]
            },
            startInfo);

        Assert.False(result.LaunchAllowed);
        Assert.Contains(result.Failures, item => item.Category == "JavaAgent"
            && item.Source == "Allowed additional path is not an ordinary file.");
    }

    [Fact]
    public void UserJvmFileReaderNormalizesAgentAndLoggingPathsOnly()
    {
        var workingDirectory = Path.Combine(TempRoot, "working");
        Directory.CreateDirectory(workingDirectory);

        var paths = FinalLaunchCommandPathReader.ReadAllowedUserFilePaths(
            "-javaagent:\"agent with spaces.jar\" -Dlog4j2.configurationFile=\"config with spaces.xml\" -Djava.library.path=natives",
            workingDirectory);

        Assert.Equal(
            [Path.Combine(workingDirectory, "agent with spaces.jar"), Path.Combine(workingDirectory, "config with spaces.xml")],
            paths);
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

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    private sealed class RecordingLoaderProvider(LoaderKind kind) : ILoaderProvider
    {
        public LoaderKind Kind { get; } = kind;
        public bool IsImplemented => true;
        public int InstallCount { get; private set; }

        public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
            string minecraftVersion,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0) =>
            Task.FromResult<IReadOnlyList<LoaderVersionInfo>>([new LoaderVersionInfo("test")]);

        public async Task<string> InstallAsync(
            string minecraftVersion,
            string gameDirectory,
            string isolatedVersionName,
            string? loaderVersion,
            IProgress<LauncherProgress>? progress,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            InstallCount++;
            var directory = Path.Combine(gameDirectory, "versions", isolatedVersionName);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"{isolatedVersionName}.json"),
                $$"""{ "id": "{{isolatedVersionName}}", "libraries": [] }""",
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"{isolatedVersionName}.jar"),
                "client",
                cancellationToken);
            return isolatedVersionName;
        }
    }

    private sealed class FailingLoaderProvider(LoaderKind kind, Exception exception) : ILoaderProvider
    {
        public LoaderKind Kind { get; } = kind;
        public bool IsImplemented => true;

        public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
            string minecraftVersion,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0) =>
            Task.FromResult<IReadOnlyList<LoaderVersionInfo>>([new LoaderVersionInfo("test")]);

        public Task<string> InstallAsync(
            string minecraftVersion,
            string gameDirectory,
            string isolatedVersionName,
            string? loaderVersion,
            IProgress<LauncherProgress>? progress,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0) =>
            Task.FromException<string>(exception);
    }
}
