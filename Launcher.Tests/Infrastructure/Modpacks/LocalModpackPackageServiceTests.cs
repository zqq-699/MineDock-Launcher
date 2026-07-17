/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Net;
using System.Text;
using Launcher.Application.Services;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Modpacks;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class LocalModpackPackageServiceTests : TestTempDirectory
{
    [Theory]
    [InlineData("modrinth.index.json", ModpackPackageKind.Modrinth, LoaderKind.Fabric)]
    [InlineData("manifest.json", ModpackPackageKind.CurseForge, LoaderKind.NeoForge)]
    public async Task PrepareParsesSupportedPackage(string manifestName, ModpackPackageKind kind, LoaderKind loader)
    {
        var path = Path.Combine(TempRoot, kind == ModpackPackageKind.Modrinth ? "pack.mrpack" : "pack.zip");
        CreateArchive(path, archive => AddEntry(archive, manifestName, kind == ModpackPackageKind.Modrinth
            ? """{"name":"Demo","dependencies":{"minecraft":"1.20.1","fabric-loader":"0.16.10"},"files":[]}"""
            : """{"name":"Demo","minecraft":{"version":"1.20.4","modLoaders":[{"id":"neoforge-20.4.237","primary":true}]},"files":[]}"""));

        var prepared = await CreateService().PrepareAsync(path);

        Assert.Equal(kind, prepared.PackageKind);
        Assert.Equal(loader, prepared.Loader);
        Assert.Equal("Demo", prepared.PackageName);
    }

    [Fact]
    public async Task PrepareRejectsPathTraversal()
    {
        var path = Path.Combine(TempRoot, "unsafe.mrpack");
        CreateArchive(path, archive =>
        {
            AddEntry(archive, "modrinth.index.json", """{"name":"Unsafe","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
            AddEntry(archive, "overrides/../evil.txt", "bad");
        });

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() => CreateService().PrepareAsync(path));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
        Assert.False(File.Exists(Path.Combine(TempRoot, "evil.txt")));
    }

    [Fact]
    public async Task PrepareRejectsCaseInsensitiveDuplicateOverrideTargets()
    {
        var path = Path.Combine(TempRoot, "duplicates.mrpack");
        CreateArchive(path, archive =>
        {
            AddEntry(archive, "modrinth.index.json", """{"name":"Duplicates","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
            AddEntry(archive, "overrides/config/Settings.txt", "first");
            AddEntry(archive, "overrides/CONFIG/settings.txt", "second");
        });

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() => CreateService().PrepareAsync(path));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
    }

    [Fact]
    public void ExtractionBudgetRejectsOversizedContent()
    {
        var exception = Assert.Throws<ModpackImportException>(() => new ZipExtractionBudget(1).Reserve(2));
        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
    }

    [Fact]
    public async Task InstallRejectsHashMismatchAndRemovesPartialFile()
    {
        var service = CreateService(new HttpClient(new FixedHandler("actual-bytes")));
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        Directory.CreateDirectory(instance.InstanceDirectory);
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files = [new PreparedModpackDownload
            {
                FileName = "mod.jar",
                RelativePath = "mods/mod.jar",
                SourceUrl = "https://download.test/mod.jar",
                Sha1 = new string('0', 40)
            }]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() =>
            service.InstallContentAsync(prepared, instance, null));

        Assert.Equal(ModpackImportFailureReason.HashMismatch, exception.FailureReason);
        Assert.Empty(Directory.EnumerateFiles(instance.InstanceDirectory, "*.download", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InstallRejectsCaseInsensitiveDuplicateTargetsBeforeDownloading()
    {
        var handler = new FixedHandler("downloaded");
        var service = CreateService(new HttpClient(handler));
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        Directory.CreateDirectory(instance.InstanceDirectory);
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            SourceArchivePath = Path.Combine(TempRoot, "pack.mrpack"),
            WorkingDirectory = Path.Combine(TempRoot, "work"),
            PackageName = "Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Vanilla,
            Files =
            [
                new PreparedModpackDownload
                {
                    FileName = "first.jar",
                    RelativePath = "mods/Same.jar",
                    SourceUrl = "https://download.test/first.jar"
                },
                new PreparedModpackDownload
                {
                    FileName = "second.jar",
                    RelativePath = "MODS/same.jar",
                    SourceUrl = "https://download.test/second.jar"
                }
            ]
        };
        Directory.CreateDirectory(prepared.WorkingDirectory);

        var exception = await Assert.ThrowsAsync<ModpackImportException>(() =>
            service.InstallContentAsync(prepared, instance, null));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
        Assert.Equal(0, handler.RequestCount);
        Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "Same.jar")));
        Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "MODS", "same.jar")));
    }

    [Fact]
    public async Task CurseForgeDownloadStartsBeforeAllFilesAreResolvedAndPublishesAfterPipelineCompletes()
    {
        var handler = new CurseForgePipelineHandler(duplicateTarget: false);
        var service = CreateService(new HttpClient(handler), apiKey: "test-key");
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        var prepared = CreateCurseForgePreparedModpack();
        var progress = new ThreadSafeProgress();

        var downloadTask = service.DownloadFilesAsync(prepared, instance, progress);
        await handler.FirstDownloadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForConditionAsync(
            () => Directory.Exists(instance.InstanceDirectory)
                && Directory.EnumerateFiles(instance.InstanceDirectory, "*.download", SearchOption.AllDirectories).Any());

        Assert.False(downloadTask.IsCompleted);
        Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "first.jar")));
        Assert.DoesNotContain(
            progress.Snapshot(),
            report => report.Stage == ImportProgressStages.ResolvingPackFiles && report.Percent == 100);

        handler.ReleaseSecondResolution();
        var manualDownloads = await downloadTask.WaitAsync(TimeSpan.FromSeconds(5));
        var reports = progress.Snapshot();
        var firstDownloadReport = Array.FindIndex(
            reports,
            report => report.Stage == ImportProgressStages.DownloadingPackFiles
                && report.Message == "first.jar");
        var resolutionCompletedReport = Array.FindIndex(
            reports,
            report => report.Stage == ImportProgressStages.ResolvingPackFiles && report.Percent == 100);

        Assert.Empty(manualDownloads);
        Assert.True(firstDownloadReport >= 0);
        Assert.True(resolutionCompletedReport > firstDownloadReport);
        Assert.Contains(reports, report => report.Stage == ImportProgressStages.ProcessingPackFiles && report.Percent == 100);
        Assert.Equal(ImportProgressStages.ProcessingPackFiles, reports[^1].Stage);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(instance.InstanceDirectory, "mods", "first.jar")));
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(instance.InstanceDirectory, "mods", "second.jar")));
        Assert.Empty(Directory.EnumerateFiles(instance.InstanceDirectory, "*.download", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CurseForgeDynamicDuplicateTargetCancelsPipelineAndCleansTemporaryFiles()
    {
        var handler = new CurseForgePipelineHandler(duplicateTarget: true);
        var service = CreateService(new HttpClient(handler), apiKey: "test-key");
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        var prepared = CreateCurseForgePreparedModpack();

        var downloadTask = service.DownloadFilesAsync(prepared, instance, null);
        await handler.FirstDownloadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        handler.ReleaseSecondResolution();

        var exception = await Assert.ThrowsAsync<ModpackImportException>(
            () => downloadTask.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ModpackImportFailureReason.InvalidManifest, exception.FailureReason);
        Assert.True(handler.DownloadRequestCount > 0);
        Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "same.jar")));
        Assert.Empty(Directory.EnumerateFiles(instance.InstanceDirectory, "*.download", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancellationStopsPipelineAndCleansTemporaryFiles()
    {
        var handler = new CancellationBlockingHandler();
        var service = CreateService(new HttpClient(handler));
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            PackageName = "Pack",
            MinecraftVersion = "1.20.1",
            Files =
            [
                new PreparedModpackDownload
                {
                    FileName = "mod.jar",
                    RelativePath = "mods/mod.jar",
                    SourceUrl = "https://download.test/mod.jar"
                }
            ]
        };
        using var cancellation = new CancellationTokenSource();

        var downloadTask = service.DownloadFilesAsync(prepared, instance, null, cancellation.Token);
        await handler.RequestStarted.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloadTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "mods", "mod.jar")));
        Assert.Empty(Directory.EnumerateFiles(instance.InstanceDirectory, "*.download", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CurseForgeManualDownloadsKeepManifestOrderWhenResolutionCompletesOutOfOrder()
    {
        var service = CreateService(new HttpClient(new FailingCurseForgeResolutionHandler()), apiKey: "test-key");
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        var prepared = CreateCurseForgePreparedModpack();
        var progress = new ThreadSafeProgress();

        var manualDownloads = await service.DownloadFilesAsync(prepared, instance, progress);

        Assert.Equal([1L, 2L], manualDownloads.Select(download => download.ProjectId).ToArray());
        Assert.DoesNotContain(
            progress.Snapshot(),
            report => report.Stage == ImportProgressStages.DownloadingPackFiles);
        Assert.Contains(
            progress.Snapshot(),
            report => report.Stage == ImportProgressStages.ProcessingPackFiles && report.Percent == 100);
    }

    [Fact]
    public async Task DownloadStatusBeginsOnlyAfterResponseBodyBytesArrive()
    {
        var handler = new BodyGateHandler("downloaded");
        var service = CreateService(new HttpClient(handler));
        var instance = new GameInstance { Name = "Pack", InstanceDirectory = Path.Combine(TempRoot, "instance") };
        var prepared = new PreparedModpack
        {
            PackageKind = ModpackPackageKind.Modrinth,
            PackageName = "Pack",
            MinecraftVersion = "1.20.1",
            Files =
            [
                new PreparedModpackDownload
                {
                    FileName = "mod.jar",
                    RelativePath = "mods/mod.jar",
                    SourceUrl = "https://download.test/mod.jar"
                }
            ]
        };
        var progress = new ThreadSafeProgress();

        var downloadTask = service.DownloadFilesAsync(prepared, instance, progress);
        await handler.RequestStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(
            progress.Snapshot(),
            report => report.Stage == ImportProgressStages.DownloadingPackFiles);

        handler.ReleaseBody();
        await downloadTask.WaitAsync(TimeSpan.FromSeconds(5));
        var reports = progress.Snapshot();

        Assert.Contains(
            reports,
            report => report.Stage == ImportProgressStages.DownloadingPackFiles
                && report.Message == "mod.jar");
        Assert.Equal(ImportProgressStages.ProcessingPackFiles, reports[^1].Stage);
    }

    [Fact]
    public async Task InstallCopiesOverridesIntoInstance()
    {
        var path = Path.Combine(TempRoot, "overrides.mrpack");
        CreateArchive(path, archive =>
        {
            AddEntry(archive, "modrinth.index.json", """{"name":"Overrides","dependencies":{"minecraft":"1.20.1"},"files":[]}""");
            AddEntry(archive, "overrides/config/settings.txt", "demo");
        });
        var service = CreateService();
        var prepared = await service.PrepareAsync(path);
        var instance = new GameInstance { Name = "Overrides", InstanceDirectory = Path.Combine(TempRoot, "instance") };

        await service.InstallContentAsync(prepared, instance, null);

        Assert.Equal("demo", await File.ReadAllTextAsync(Path.Combine(instance.InstanceDirectory, "config", "settings.txt")));
    }

    private LocalModpackPackageService CreateService(HttpClient? client = null, string? apiKey = null)
    {
        var paths = new LauncherPathProvider(TempRoot);
        return new LocalModpackPackageService(paths, httpClient: client,
            curseForgeApiKeyResolver: new CurseForgeApiKeyResolver(
                paths,
                embeddedApiKeyProvider: _ => Task.FromResult(apiKey)));
    }

    private static PreparedModpack CreateCurseForgePreparedModpack() => new()
    {
        PackageKind = ModpackPackageKind.CurseForge,
        PackageName = "Pack",
        MinecraftVersion = "1.20.1",
        Files =
        [
            new PreparedModpackDownload { ProjectId = 1, FileId = 101, TargetDirectory = "mods" },
            new PreparedModpackDownload { ProjectId = 2, FileId = 202, TargetDirectory = "mods" }
        ]
    };

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeoutAt)
                throw new TimeoutException("The expected condition was not reached.");
            await Task.Delay(20);
        }
    }

    private static void CreateArchive(string path, Action<ZipArchive> configure)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        configure(archive);
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class FixedHandler(string content) : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(CreateResponse());

        private HttpResponseMessage CreateResponse()
        {
            Interlocked.Increment(ref requestCount);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
        }
    }

    private sealed class CurseForgePipelineHandler(bool duplicateTarget) : HttpMessageHandler
    {
        private readonly TaskCompletionSource firstDownloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseSecondResolution = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int downloadRequestCount;

        public Task FirstDownloadStarted => firstDownloadStarted.Task;

        public int DownloadRequestCount => Volatile.Read(ref downloadRequestCount);

        public void ReleaseSecondResolution() => releaseSecondResolution.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (string.Equals(uri.Host, "api.curseforge.com", StringComparison.OrdinalIgnoreCase))
            {
                var isSecondFile = uri.AbsolutePath.Contains("/mods/2/", StringComparison.Ordinal);
                if (isSecondFile && uri.AbsolutePath.EndsWith("/download-url", StringComparison.Ordinal))
                    await releaseSecondResolution.Task.WaitAsync(cancellationToken);

                var fileName = duplicateTarget
                    ? "same.jar"
                    : isSecondFile ? "second.jar" : "first.jar";
                if (uri.AbsolutePath.EndsWith("/download-url", StringComparison.Ordinal))
                    return JsonResponse($"{{\"data\":\"https://download.test/{fileName}\"}}");

                return JsonResponse(
                    $"{{\"data\":{{\"displayName\":\"{fileName}\",\"fileName\":\"{fileName}\",\"downloadUrl\":\"https://download.test/{fileName}\",\"hashes\":[]}}}}");
            }

            Interlocked.Increment(ref downloadRequestCount);
            if (uri.AbsolutePath.EndsWith("first.jar", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("same.jar", StringComparison.Ordinal))
            {
                firstDownloadStarted.TrySetResult();
            }

            var content = uri.AbsolutePath.EndsWith("second.jar", StringComparison.Ordinal) ? "second" : "first";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
        }
    }

    private sealed class FailingCurseForgeResolutionHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/mods/1/", StringComparison.Ordinal))
                await Task.Delay(80, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private sealed class CancellationBlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStarted => requestStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation test request unexpectedly completed.");
        }
    }

    private sealed class BodyGateHandler(string content) : HttpMessageHandler
    {
        private readonly TaskCompletionSource requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseBody = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStarted => requestStarted.Task;

        public void ReleaseBody() => releaseBody.TrySetResult();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requestStarted.TrySetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BodyGateStream(Encoding.UTF8.GetBytes(content), releaseBody.Task))
            });
        }
    }

    private sealed class BodyGateStream(byte[] content, Task release) : Stream
    {
        private readonly MemoryStream inner = new(content, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            release.GetAwaiter().GetResult();
            return inner.Read(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await release.WaitAsync(cancellationToken);
            return await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await release.WaitAsync(cancellationToken);
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ThreadSafeProgress : IProgress<LauncherProgress>
    {
        private readonly object syncRoot = new();
        private readonly List<LauncherProgress> reports = [];

        public void Report(LauncherProgress value)
        {
            lock (syncRoot)
                reports.Add(value);
        }

        public LauncherProgress[] Snapshot()
        {
            lock (syncRoot)
                return reports.ToArray();
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
