/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.Net;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class ManagedDownloadPathGuardTests : TestTempDirectory
{
    [Theory]
    [InlineData("libraries")]
    [InlineData("versions/Guarded Version")]
    public async Task ManagedRepairRootReparsePointFailsBeforeNetworkOrExternalWrite(string relativeRoot)
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var managedRoot = Path.Combine(
            minecraftDirectory,
            relativeRoot.Replace('/', Path.DirectorySeparatorChar));
        var externalDirectory = Path.Combine(TempRoot, "external", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(managedRoot)!);
        Directory.CreateDirectory(externalDirectory);
        var externalFile = Path.Combine(externalDirectory, "artifact.bin");
        await File.WriteAllTextAsync(externalFile, "external-original");
        if (!TryCreateDirectoryLink(managedRoot, externalDirectory))
            return;

        var handler = new CountingHandler();
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var batch = new ManagedVersionRepairDownloadBatch(
            client,
            downloadSpeedLimitState: null,
            logger: NullLogger.Instance,
            sourcePreference: DownloadSourcePreference.Official,
            speedLimitMbPerSecond: 0,
            progress: null);

        try
        {
            var exception = await Assert.ThrowsAsync<InstanceRepairException>(() => batch.DownloadAsync(
                new RepairDownloadRequest(
                    "https://piston-data.mojang.com/v1/objects/example/artifact.bin",
                    Path.Combine(managedRoot, "artifact.bin"),
                    "Mojang",
                    LibraryName: null,
                    ArtifactPath: null,
                    ManagedRoot: managedRoot),
                CancellationToken.None));

            Assert.IsType<InvalidDataException>(exception.InnerException);
            Assert.Equal(0, handler.RequestCount);
            Assert.Equal("external-original", await File.ReadAllTextAsync(externalFile));
        }
        finally
        {
            DeleteDirectoryLink(managedRoot);
        }
    }

    [Fact]
    public async Task AtomicLoaderPublicationRejectsIntermediateReparsePointBeforeExternalWrite()
    {
        var managedRoot = Path.Combine(TempRoot, ".minecraft");
        var libraries = Path.Combine(managedRoot, "libraries");
        var externalDirectory = Path.Combine(TempRoot, "external-libraries");
        Directory.CreateDirectory(managedRoot);
        Directory.CreateDirectory(externalDirectory);
        var externalTarget = Path.Combine(externalDirectory, "example.jar");
        await File.WriteAllTextAsync(externalTarget, "external-original");
        if (!TryCreateDirectoryLink(libraries, externalDirectory))
            return;
        var source = Path.Combine(TempRoot, "trusted.jar");
        await File.WriteAllTextAsync(source, "trusted-replacement");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => AtomicSharedFilePublisher.PublishCopyAsync(
                source,
                Path.Combine(libraries, "example.jar"),
                expectedSha1: null,
                cancellationToken: CancellationToken.None,
                managedRoot: managedRoot));

            Assert.Equal("external-original", await File.ReadAllTextAsync(externalTarget));
            Assert.Empty(Directory.EnumerateFiles(externalDirectory, ".*.tmp"));
        }
        finally
        {
            DeleteDirectoryLink(libraries);
        }
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            if (!OperatingSystem.IsWindows())
                return false;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath }
        });
        process?.WaitForExit();
        return process is { ExitCode: 0 }
            && Directory.Exists(linkPath)
            && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
    }

    private static void DeleteDirectoryLink(string linkPath)
    {
        if (Directory.Exists(linkPath)
            && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(linkPath, recursive: false);
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent("network-content")
            });
        }
    }
}
