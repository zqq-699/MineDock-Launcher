/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Launcher.Infrastructure.Multiplayer;
using RuntimeArchitecture = System.Runtime.InteropServices.Architecture;

namespace Launcher.Tests.Infrastructure.Multiplayer;

public sealed class EasyTierProvisioningServiceTests
{
    [Fact]
    public async Task EnsureAvailableDownloadsVerifiesAndPublishesOnlyRequiredFiles()
    {
        var archive = CreateArchive(includeAllRequiredFiles: true, includeUnrelatedFile: true);
        using var context = CreateContext(archive);

        var module = await context.Service.EnsureAvailableAsync();

        Assert.Equal(1, context.Handler.RequestCount);
        Assert.True(File.Exists(module.CoreExecutablePath));
        Assert.True(File.Exists(module.CliExecutablePath));
        Assert.True(File.Exists(module.PacketLibraryPath));
        Assert.False(File.Exists(Path.Combine(module.DirectoryPath, "unrelated.txt")));
        Assert.NotNull(context.Service.TryGetAvailable());
    }

    [Fact]
    public async Task EnsureAvailableRejectsChecksumMismatchWithoutPublishingModule()
    {
        var archive = CreateArchive(includeAllRequiredFiles: true);
        using var context = CreateContext(archive, expectedSha256: new string('0', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Service.EnsureAvailableAsync());

        Assert.Null(context.Service.TryGetAvailable());
        Assert.Empty(Directory.EnumerateDirectories(context.ModuleRoot, "easytier-windows-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task EnsureAvailableRejectsArchiveMissingRequiredFile()
    {
        var archive = CreateArchive(includeAllRequiredFiles: false);
        using var context = CreateContext(archive);

        await Assert.ThrowsAsync<InvalidDataException>(() => context.Service.EnsureAvailableAsync());

        Assert.Null(context.Service.TryGetAvailable());
    }

    [Fact]
    public async Task PublishedModuleIsReusedAndMissingFileInvalidatesIt()
    {
        var archive = CreateArchive(includeAllRequiredFiles: true);
        using var context = CreateContext(archive);
        var module = await context.Service.EnsureAvailableAsync();

        var reused = await context.Service.EnsureAvailableAsync();
        File.Delete(module.PacketLibraryPath);

        Assert.Equal(module, reused);
        Assert.Equal(1, context.Handler.RequestCount);
        Assert.Null(context.Service.TryGetAvailable());
    }

    [Fact]
    public async Task ConcurrentProvisioningDownloadsOnlyOnce()
    {
        var archive = CreateArchive(includeAllRequiredFiles: true);
        using var context = CreateContext(archive, responseDelay: TimeSpan.FromMilliseconds(40));

        var modules = await Task.WhenAll(
            context.Service.EnsureAvailableAsync(),
            context.Service.EnsureAvailableAsync(),
            context.Service.EnsureAvailableAsync());

        Assert.All(modules, module => Assert.True(File.Exists(module.CoreExecutablePath)));
        Assert.Equal(1, context.Handler.RequestCount);
    }

    [Fact]
    public async Task PrimaryFailureFallsBackToSecondSourceInOrder()
    {
        var archive = CreateArchive(includeAllRequiredFiles: true);
        using var context = CreateContext(
            archive,
            includeFallbackSource: true,
            failuresBeforeSuccess: 1);

        var module = await context.Service.EnsureAvailableAsync();

        Assert.True(File.Exists(module.CoreExecutablePath));
        Assert.Equal(2, context.Handler.RequestCount);
        Assert.Equal(
            ["primary.example.invalid", "fallback.example.invalid"],
            context.Handler.RequestHosts);
    }

    [Fact]
    public async Task RedirectResponseIsFollowedBeforeDownloadingArchive()
    {
        var archive = CreateArchive(includeAllRequiredFiles: true);
        using var context = CreateContext(archive, redirectFirstRequest: true);

        var module = await context.Service.EnsureAvailableAsync();

        Assert.True(File.Exists(module.CoreExecutablePath));
        Assert.Equal(2, context.Handler.RequestCount);
        Assert.Equal(
            ["example.invalid", "redirected.example.invalid"],
            context.Handler.RequestHosts);
    }

    [Theory]
    [InlineData(RuntimeArchitecture.X64, "x86_64")]
    [InlineData(RuntimeArchitecture.Arm64, "arm64")]
    public void ProductionDistributionUsesOnlyOfficialGitHub(
        RuntimeArchitecture architecture,
        string assetArchitecture)
    {
        var distribution = EasyTierProvisioningService.ResolveDistribution(architecture);

        Assert.Equal(assetArchitecture, distribution.Architecture);
        var downloadUri = Assert.Single(distribution.DownloadUris);
        Assert.Equal("github.com", downloadUri.Host);
        Assert.Contains($"easytier-windows-{assetArchitecture}-v2.6.4.zip", distribution.DownloadUris[0].AbsoluteUri);
    }

    private static TestContext CreateContext(
        byte[] archive,
        string? expectedSha256 = null,
        TimeSpan? responseDelay = null,
        bool includeFallbackSource = false,
        int failuresBeforeSuccess = 0,
        bool redirectFirstRequest = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"bhl-easytier-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var handler = new RecordingHttpMessageHandler(
            archive,
            responseDelay,
            failuresBeforeSuccess,
            redirectFirstRequest);
        IReadOnlyList<Uri> downloadUris = includeFallbackSource
            ?
            [
                new Uri("https://primary.example.invalid/easytier.zip"),
                new Uri("https://fallback.example.invalid/easytier.zip")
            ]
            : [new Uri("https://example.invalid/easytier.zip")];
        var distribution = new EasyTierProvisioningService.EasyTierDistribution(
            "x86_64",
            downloadUris,
            expectedSha256 ?? Convert.ToHexString(SHA256.HashData(archive)));
        var service = new EasyTierProvisioningService(new HttpClient(handler), root, distribution);
        return new TestContext(root, handler, service);
    }

    private static byte[] CreateArchive(bool includeAllRequiredFiles, bool includeUnrelatedFile = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "easytier-windows-x86_64/easytier-core.exe", [0x4d, 0x5a, 1, 2, 3]);
            AddEntry(archive, "easytier-windows-x86_64/easytier-cli.exe", [0x4d, 0x5a, 4, 5, 6]);
            if (includeAllRequiredFiles)
                AddEntry(archive, "easytier-windows-x86_64/Packet.dll", [0x4d, 0x5a, 7, 8, 9]);
            if (includeUnrelatedFile)
                AddEntry(archive, "easytier-windows-x86_64/unrelated.txt", [10, 11, 12]);
        }
        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var target = entry.Open();
        target.Write(content);
    }

    private sealed class RecordingHttpMessageHandler(
        byte[] content,
        TimeSpan? responseDelay,
        int failuresBeforeSuccess,
        bool redirectFirstRequest) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<string> RequestHosts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestHosts.Add(request.RequestUri!.Host);
            if (responseDelay is { } delay)
                await Task.Delay(delay, cancellationToken);
            if (redirectFirstRequest && RequestCount == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://redirected.example.invalid/easytier.zip");
                return redirect;
            }
            if (RequestCount <= failuresBeforeSuccess)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
        }
    }

    private sealed class TestContext(
        string moduleRoot,
        RecordingHttpMessageHandler handler,
        EasyTierProvisioningService service) : IDisposable
    {
        public string ModuleRoot { get; } = moduleRoot;
        public RecordingHttpMessageHandler Handler { get; } = handler;
        public EasyTierProvisioningService Service { get; } = service;

        public void Dispose()
        {
            if (Directory.Exists(ModuleRoot))
                Directory.Delete(ModuleRoot, recursive: true);
        }
    }
}
