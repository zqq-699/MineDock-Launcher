/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Infrastructure.Minecraft;
using Launcher.Infrastructure.Modpacks;
using Launcher.Infrastructure.Modrinth;

namespace Launcher.Tests.Infrastructure.Modrinth;

public sealed class ModrinthServiceTests : TestTempDirectory
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("trusted-mod-content");

    [Fact]
    public async Task ValidSha512DownloadPublishesAtomicallyAndReportsProgress()
    {
        var handler = new ModrinthHandler(CreateVersionJson("example.jar", Payload));
        var limiter = new RecordingLimiter();
        var service = CreateService(handler, limiter);
        var instance = CreateInstance();
        var reports = new List<LauncherProgress>();

        var path = await service.InstallLatestCompatibleAsync(
            CreateProject(),
            instance,
            new InlineProgress(reports));

        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.Equal(1, handler.DownloadRequestCount);
        Assert.Equal(1, limiter.ModpackAcquisitions);
        Assert.Contains(reports, report => report.Stage == ModProgressStages.DownloadingFile);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".*.bhl-pending-*.tmp"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InvalidSizeOrHashPreservesExistingMod(bool invalidSize, bool invalidHash)
    {
        var expectedPayload = invalidHash ? Encoding.UTF8.GetBytes("different-content") : Payload;
        var declaredSize = invalidSize ? Payload.Length + 1L : Payload.Length;
        var handler = new ModrinthHandler(CreateVersionJson(
            "example.jar",
            expectedPayload,
            declaredSize: declaredSize), Payload);
        var service = CreateService(handler);
        var instance = CreateInstance();
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(target, "existing-valid-mod");

        await Assert.ThrowsAnyAsync<Exception>(() => service.InstallLatestCompatibleAsync(
            CreateProject(), instance, progress: null));

        Assert.Equal("existing-valid-mod", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.EnumerateFiles(modsDirectory, ".*.bhl-pending-*.tmp"));
    }

    [Fact]
    public async Task CancellationDuringBodyPreservesExistingModAndRemovesTemporaryFile()
    {
        var body = new BlockingAfterFirstReadStream(Payload);
        var handler = new ModrinthHandler(CreateVersionJson("example.jar", Payload), downloadStream: body);
        var service = CreateService(handler);
        var instance = CreateInstance();
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(target, "existing-valid-mod");
        using var cancellation = new CancellationTokenSource();

        var installTask = service.InstallLatestCompatibleAsync(
            CreateProject(), instance, progress: null, cancellation.Token);
        await body.FirstRead.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installTask);
        Assert.Equal("existing-valid-mod", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.EnumerateFiles(modsDirectory, ".*.bhl-pending-*.tmp"));
    }

    [Fact]
    public async Task InterruptedBodyPreservesExistingModAndRemovesTemporaryFile()
    {
        var handler = new ModrinthHandler(
            CreateVersionJson("example.jar", Payload),
            downloadStream: new InterruptingReadStream(Payload));
        var service = CreateService(handler);
        var instance = CreateInstance();
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(target, "existing-valid-mod");

        await Assert.ThrowsAnyAsync<Exception>(() => service.InstallLatestCompatibleAsync(
            CreateProject(), instance, progress: null));

        Assert.Equal("existing-valid-mod", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.EnumerateFiles(modsDirectory, ".*.bhl-pending-*.tmp"));
    }

    [Fact]
    public async Task LockedExistingModIsPreservedWhenAtomicPublishFails()
    {
        var handler = new ModrinthHandler(CreateVersionJson("example.jar", Payload));
        var service = CreateService(handler);
        var instance = CreateInstance();
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var target = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(target, "existing-valid-mod");
        await using var lockStream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<DownloadLocalFileException>(() => service.InstallLatestCompatibleAsync(
            CreateProject(), instance, progress: null));

        Assert.Equal(1, handler.DownloadRequestCount);
        Assert.Equal("existing-valid-mod", Encoding.UTF8.GetString(await ReadLockedFileAsync(lockStream)));
        Assert.Empty(Directory.EnumerateFiles(modsDirectory, ".*.bhl-pending-*.tmp"));
    }

    [Fact]
    public async Task UnsafeFileNameIsRejectedBeforeFileRequestOrWrite()
    {
        var handler = new ModrinthHandler(CreateVersionJson("../escaped.jar", Payload));
        var service = CreateService(handler);
        var instance = CreateInstance();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLatestCompatibleAsync(
            CreateProject(), instance, progress: null));

        Assert.Equal(0, handler.DownloadRequestCount);
        Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory, "escaped.jar")));
    }

    [Theory]
    [InlineData("size")]
    [InlineData("sha512")]
    [InlineData("sha512-null")]
    [InlineData("hashes-null")]
    public async Task MissingSizeOrInvalidHashesAreRejectedBeforeFileRequest(string invalidField)
    {
        var metadata = JsonNode.Parse(CreateVersionJson("example.jar", Payload))!;
        var file = metadata[0]!["files"]![0]!.AsObject();
        if (invalidField == "size")
            file.Remove("size");
        else if (invalidField == "sha512-null")
            file["hashes"]!["sha512"] = null;
        else if (invalidField == "hashes-null")
            file["hashes"] = null;
        else
            file["hashes"]!["sha512"] = "not-a-valid-sha512";
        var handler = new ModrinthHandler(metadata.ToJsonString());
        var service = CreateService(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLatestCompatibleAsync(
            CreateProject(), CreateInstance(), progress: null));

        Assert.Equal(0, handler.DownloadRequestCount);
    }

    [Fact]
    public async Task ModsReparsePointIsRejectedBeforeFileRequestOrExternalWriteWhenSupported()
    {
        var handler = new ModrinthHandler(CreateVersionJson("example.jar", Payload));
        var service = CreateService(handler);
        var instance = CreateInstance();
        Directory.CreateDirectory(instance.InstanceDirectory);
        var externalDirectory = Path.Combine(TempRoot, "external-mods");
        Directory.CreateDirectory(externalDirectory);
        var externalFile = Path.Combine(externalDirectory, "example.jar");
        await File.WriteAllTextAsync(externalFile, "external-original");
        var modsDirectory = Path.Combine(instance.InstanceDirectory, "mods");

        try
        {
            Directory.CreateSymbolicLink(modsDirectory, externalDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallLatestCompatibleAsync(
                CreateProject(), instance, progress: null));

            Assert.Equal(0, handler.DownloadRequestCount);
            Assert.Equal("external-original", await File.ReadAllTextAsync(externalFile));
        }
        finally
        {
            if (Directory.Exists(modsDirectory))
                Directory.Delete(modsDirectory, recursive: false);
        }
    }

    private ModrinthService CreateService(ModrinthHandler handler, IImportConcurrencyLimiter? limiter = null)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new ModrinthService(
            client,
            logger: null,
            settingsService: null,
            downloadSpeedLimitState: null,
            limiter: limiter ?? new ImportConcurrencyLimiter(),
            retryOptions: new DownloadRetryOptions
            {
                MaxAttemptsPerSource = 1,
                RetryDelay = TimeSpan.Zero,
                ResponseHeadersTimeout = TimeSpan.FromSeconds(1),
                FirstByteTimeout = TimeSpan.FromSeconds(1),
                BodyIdleTimeout = TimeSpan.FromSeconds(1)
            });
    }

    private GameInstance CreateInstance() => new()
    {
        Id = "test-instance",
        Name = "Test Instance",
        MinecraftVersion = "1.20.1",
        VersionName = "1.20.1-fabric",
        InstanceDirectory = Path.Combine(TempRoot, "instance"),
        Loader = LoaderKind.Fabric
    };

    private static ModrinthProject CreateProject() => new()
    {
        ProjectId = "example-project",
        Slug = "example-project",
        Title = "Example Mod"
    };

    private static string CreateVersionJson(string fileName, byte[] hashPayload, long? declaredSize = null)
    {
        return JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "version-id",
                project_id = "example-project",
                version_number = "1.0.0",
                version_type = "release",
                files = new[]
                {
                    new
                    {
                        filename = fileName,
                        url = "https://cdn.modrinth.com/data/example/version/example.jar",
                        primary = true,
                        size = declaredSize ?? Payload.Length,
                        hashes = new
                        {
                            sha512 = Convert.ToHexString(SHA512.HashData(hashPayload)).ToLowerInvariant(),
                            sha1 = Convert.ToHexString(SHA1.HashData(hashPayload)).ToLowerInvariant()
                        }
                    }
                }
            }
        });
    }

    private static async Task<byte[]> ReadLockedFileAsync(FileStream stream)
    {
        stream.Position = 0;
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes);
        return bytes;
    }

    private sealed class ModrinthHandler(
        string versionJson,
        byte[]? downloadPayload = null,
        Stream? downloadStream = null) : HttpMessageHandler
    {
        public int DownloadRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent content;
            if (request.RequestUri!.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase))
            {
                content = new StringContent(versionJson, Encoding.UTF8, "application/json");
            }
            else
            {
                DownloadRequestCount++;
                content = downloadStream is null
                    ? new ByteArrayContent(downloadPayload ?? Payload)
                    : new StreamContent(downloadStream);
                content.Headers.ContentLength = Payload.Length;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        }
    }

    private sealed class BlockingAfterFirstReadStream(byte[] payload) : Stream
    {
        private readonly TaskCompletionSource firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool returnedFirstByte;

        public Task FirstRead => firstRead.Task;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => payload.Length;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!returnedFirstByte)
            {
                returnedFirstByte = true;
                buffer.Span[0] = payload[0];
                firstRead.TrySetResult();
                return 1;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class InterruptingReadStream(byte[] payload) : Stream
    {
        private bool returnedFirstByte;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => payload.Length;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!returnedFirstByte)
            {
                returnedFirstByte = true;
                buffer.Span[0] = payload[0];
                return ValueTask.FromResult(1);
            }

            return ValueTask.FromException<int>(new IOException("Injected response interruption."));
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class InlineProgress(List<LauncherProgress> reports) : IProgress<LauncherProgress>
    {
        public void Report(LauncherProgress value) => reports.Add(value);
    }

    private sealed class RecordingLimiter : IImportConcurrencyLimiter
    {
        public int ModpackAcquisitions { get; private set; }
        public ValueTask<IImportConcurrencyLease> AcquireMetadataSlotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IImportConcurrencyLease>(new Lease());
        public ValueTask<IImportConcurrencyLease> AcquireRuntimeDownloadSlotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IImportConcurrencyLease>(new Lease());
        public ValueTask<IImportConcurrencyLease> AcquireHashSlotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IImportConcurrencyLease>(new Lease());
        public ValueTask<IImportConcurrencyLease> AcquireModpackDownloadSlotAsync(CancellationToken cancellationToken = default)
        {
            ModpackAcquisitions++;
            return ValueTask.FromResult<IImportConcurrencyLease>(new Lease());
        }

        private sealed class Lease : IImportConcurrencyLease
        {
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
