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

    private sealed class FailingLoaderProvider(LoaderKind kind, Exception exception) : ILoaderProvider
    {
        public LoaderKind Kind { get; } = kind;
        public bool IsImplemented => true;

        public Task<IReadOnlyList<LoaderVersionInfo>> GetLoaderVersionsAsync(
            string minecraftVersion,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0) =>
            Task.FromResult<IReadOnlyList<LoaderVersionInfo>>([new LoaderVersionInfo("test")]);

        public Task<string> InstallAsync(
            string minecraftVersion,
            string gameDirectory,
            string isolatedVersionName,
            string? loaderVersion,
            IProgress<LauncherProgress>? progress,
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0) =>
            Task.FromException<string>(exception);
    }
}
