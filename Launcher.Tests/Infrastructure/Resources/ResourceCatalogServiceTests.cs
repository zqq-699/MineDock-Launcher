/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Services;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Resources;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class ResourceCatalogServiceTests : TestTempDirectory
{
    [Fact]
    public async Task SearchStartsAllSelectedProvidersConcurrently()
    {
        var handler = new ConcurrentProviderSearchHandler();
        var service = CreateService(handler, "key");

        var searchTask = service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Mod
        });

        try
        {
            await handler.AllProvidersStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, handler.StartedCount);
        }
        finally
        {
            handler.Release.TrySetResult();
        }

        var result = await searchTask;
        Assert.Equal(2, result.Projects.Count);
    }

    [Fact]
    public async Task CurseForgeMultiVersionSearchLimitsConcurrencyToFour()
    {
        var handler = new BlockingCurseForgeSearchHandler();
        var service = CreateService(handler, "key");
        var searchTask = service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.CurseForge,
            MinecraftVersions = ["1.20", "1.20.1", "1.20.2", "1.20.3", "1.20.4", "1.20.5"]
        });

        try
        {
            await handler.FirstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(4, handler.MaxActiveRequests);
        }
        finally
        {
            handler.Release.TrySetResult();
        }

        var result = await searchTask;
        Assert.Equal(6, handler.RequestCount);
        Assert.Equal(4, handler.MaxActiveRequests);
        Assert.Single(result.Projects);
    }

    [Fact]
    public async Task CurseForgeMultiVersionSearchPreservesRequestedVersionOrderWhenDeduplicating()
    {
        var handler = new OutOfOrderCurseForgeSearchHandler();
        var service = CreateService(handler, "key");

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.CurseForge,
            MinecraftVersions = ["first", "second"]
        });

        Assert.Equal("First version project", Assert.Single(result.Projects).Title);
    }

    [Fact]
    public async Task CurseForgeMultiVersionSearchCancelsAllInFlightRequests()
    {
        var handler = new BlockingCurseForgeSearchHandler();
        var service = CreateService(handler, "key");
        using var cancellation = new CancellationTokenSource();
        var searchTask = service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.CurseForge,
            MinecraftVersions = ["1", "2", "3", "4", "5", "6"]
        }, cancellation.Token);

        await handler.FirstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => searchTask);
        Assert.Equal(0, handler.ActiveRequests);
    }

    [Fact]
    public async Task SearchMergesBothSourcesByDownloads()
    {
        var handler = new StubHandler(request => Json(request.RequestUri!.Host == "api.modrinth.com"
            ? """{"hits":[{"project_id":"m","slug":"modrinth","title":"Modrinth","description":"","downloads":50}]}"""
            : """{"data":[{"id":9,"name":"CurseForge","slug":"curseforge","summary":"","downloadCount":120,"links":null,"logo":null}]}"""));
        var service = CreateService(handler, "key");

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest());

        Assert.Equal(["CurseForge", "Modrinth"], result.Projects.Select(project => project.Title));
        Assert.Equal([120, 50], result.Projects.Select(project => project.Downloads));
    }

    [Fact]
    public async Task VersionsMapRequiredModrinthDependencies()
    {
        var handler = new StubHandler(request => Json(request.RequestUri!.AbsolutePath switch
        {
            "/v2/projects" => """[{"id":"dep","slug":"library","project_type":"mod","title":"Library","description":"","downloads":1,"game_versions":["1.20.1"],"loaders":["fabric"]}]""",
            _ => """[{"id":"v1","name":"Main 1.0","version_number":"1.0","version_type":"release","date_published":"2024-01-01T00:00:00Z","downloads":1,"game_versions":["1.20.1"],"loaders":["fabric"],"dependencies":[{"project_id":"dep","version_id":"dep-v1","dependency_type":"required"}],"files":[{"filename":"main.jar","url":"https://download.test/main.jar","primary":true}]}]"""
        }));
        var service = CreateService(handler);

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "main",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric
        });

        var dependency = Assert.Single(Assert.Single(result.Versions).RequiredDependencies);
        Assert.Equal("dep", dependency.Project.ProjectId);
        Assert.Equal("dep-v1", dependency.VersionId);
    }

    [Fact]
    public async Task ModrinthVersionsPreserveFileIntegrityMetadata()
    {
        const string sha512 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string sha1 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var handler = new StubHandler(_ => Json(
            $$$"""[{"id":"v1","name":"Main","version_number":"1","version_type":"release","files":[{"filename":"main.jar","url":"https://download.test/main.jar","primary":true,"size":123,"hashes":{"sha512":"{{{sha512}}}","sha1":"{{{sha1}}}"}}]}]"""));
        var service = CreateService(handler);

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "main",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric
        });

        var version = Assert.Single(result.Versions);
        Assert.Equal(123, version.ExpectedFileSize);
        Assert.Contains(version.FileHashes, hash => hash.Algorithm == ResourceFileHashAlgorithm.Sha512 && hash.Value == sha512);
        Assert.Contains(version.FileHashes, hash => hash.Algorithm == ResourceFileHashAlgorithm.Sha1 && hash.Value == sha1);
    }

    [Fact]
    public async Task CurseForgeVersionsPreserveFileIntegrityMetadata()
    {
        const string sha1 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string md5 = "cccccccccccccccccccccccccccccccc";
        var handler = new StubHandler(_ => Json(
            $$$"""{"data":[{"id":7,"displayName":"Pack","fileName":"pack.zip","downloadUrl":"https://download.test/pack.zip","fileLength":456,"hashes":[{"value":"{{{sha1}}}","algo":1},{"value":"{{{md5}}}","algo":2}]}],"pagination":{"totalCount":1}}"""));
        var service = CreateService(handler, "key");

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = ResourceProjectKind.ResourcePack,
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "42",
            IncludeAllVersions = true
        });

        var version = Assert.Single(result.Versions);
        Assert.Equal(456, version.ExpectedFileSize);
        Assert.Contains(version.FileHashes, hash => hash.Algorithm == ResourceFileHashAlgorithm.Sha1 && hash.Value == sha1);
        Assert.Contains(version.FileHashes, hash => hash.Algorithm == ResourceFileHashAlgorithm.Md5 && hash.Value == md5);
    }

    [Fact]
    public async Task DownloadFallsBackAfterPrimaryFailure()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.Contains("fallback", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fallback") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var path = await service.DownloadProjectVersionAsync(new ResourceProjectVersion
        {
            VersionId = "v1",
            FileName = "mod.jar",
            PrimaryDownloadUrl = "https://download.test/missing.jar",
            FallbackDownloadUrls = ["https://download.test/fallback.jar"],
            ExpectedFileSize = Encoding.UTF8.GetByteCount("fallback"),
            FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "fallback")]
        }, TempRoot);

        Assert.Equal("fallback", await File.ReadAllTextAsync(path));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task InstallWritesModIntoInstanceDirectory()
    {
        var service = CreateService(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("jar") }));
        var instance = new GameInstance { Id = "instance", InstanceDirectory = TempRoot };

        var path = await service.InstallProjectVersionAsync(new ResourceProjectVersion
        {
            VersionId = "v1",
            FileName = "mod.jar",
            PrimaryDownloadUrl = "https://download.test/mod.jar",
            ExpectedFileSize = Encoding.UTF8.GetByteCount("jar"),
            FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "jar")]
        }, instance);

        Assert.Equal(Path.Combine(TempRoot, "mods", "mod.jar"), path);
        Assert.Equal("jar", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task DownloadFallsBackAfterIntegrityMismatch()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("fallback", StringComparison.Ordinal)
                ? "expected"
                : "modified")
        });
        var service = CreateService(handler);

        var path = await service.DownloadProjectVersionAsync(new ResourceProjectVersion
        {
            VersionId = "v1",
            FileName = "mod.jar",
            PrimaryDownloadUrl = "https://download.test/primary.jar",
            FallbackDownloadUrls = ["https://download.test/fallback.jar"],
            ExpectedFileSize = Encoding.UTF8.GetByteCount("expected"),
            FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "expected")]
        }, TempRoot);

        Assert.Equal("expected", await File.ReadAllTextAsync(path));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task HashMismatchPreservesExistingFileAndCleansTemporaryFile()
    {
        Directory.CreateDirectory(TempRoot);
        var target = Path.Combine(TempRoot, "mod.jar");
        await File.WriteAllTextAsync(target, "existing");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("modified")
        });
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<ResourceProjectIntegrityException>(() =>
            service.DownloadProjectVersionAsync(new ResourceProjectVersion
            {
                VersionId = "v1",
                FileName = "mod.jar",
                PrimaryDownloadUrl = "https://download.test/mod.jar",
                ExpectedFileSize = Encoding.UTF8.GetByteCount("modified"),
                FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "expected")]
            }, TempRoot));

        Assert.Equal(ResourceProjectIntegrityFailureReason.HashMismatch, exception.Reason);
        Assert.Equal("existing", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(TempRoot, "*.download"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abcde")]
    public async Task LengthMismatchRejectsTruncatedOrOversizedContent(string content)
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        }));

        var exception = await Assert.ThrowsAsync<ResourceProjectIntegrityException>(() =>
            service.DownloadProjectVersionAsync(new ResourceProjectVersion
            {
                Kind = ResourceProjectKind.ResourcePack,
                VersionId = "v1",
                FileName = "pack.zip",
                PrimaryDownloadUrl = "https://download.test/pack.zip",
                ExpectedFileSize = 4
            }, TempRoot));

        Assert.Equal(ResourceProjectIntegrityFailureReason.LengthMismatch, exception.Reason);
        Assert.False(File.Exists(Path.Combine(TempRoot, "pack.zip")));
        Assert.Empty(Directory.GetFiles(TempRoot, "*.download"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutableResourceWithoutTrustedHashIsRejectedBeforeDownload(bool md5Only)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Download must not start."));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<ResourceProjectIntegrityException>(() =>
            service.DownloadProjectVersionAsync(new ResourceProjectVersion
            {
                VersionId = "v1",
                FileName = "mod.jar",
                PrimaryDownloadUrl = "https://download.test/mod.jar",
                ExpectedFileSize = 3,
                FileHashes = md5Only ? [CreateHash(ResourceFileHashAlgorithm.Md5, "jar")] : []
            }, TempRoot));

        Assert.Equal(ResourceProjectIntegrityFailureReason.MissingTrustedHash, exception.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NonExecutableResourceAllowsLengthOnlyVerification()
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("pack")
        }));

        var path = await service.DownloadProjectVersionAsync(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.ResourcePack,
            VersionId = "v1",
            FileName = "pack.zip",
            PrimaryDownloadUrl = "https://download.test/pack.zip",
            ExpectedFileSize = 4
        }, TempRoot);

        Assert.Equal("pack", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task MalformedHashMetadataIsRejectedBeforeDownload()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Download must not start."));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<ResourceProjectIntegrityException>(() =>
            service.DownloadProjectVersionAsync(new ResourceProjectVersion
            {
                Kind = ResourceProjectKind.ResourcePack,
                VersionId = "v1",
                FileName = "pack.zip",
                PrimaryDownloadUrl = "https://download.test/pack.zip",
                FileHashes = [new ResourceFileHash(ResourceFileHashAlgorithm.Sha1, "not-a-hash")]
            }, TempRoot));

        Assert.Equal(ResourceProjectIntegrityFailureReason.InvalidMetadata, exception.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CancellationCleansTemporaryFileWithoutPublishingTarget()
    {
        var stream = new CancelableDownloadStream();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        });
        var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource();
        var download = service.DownloadProjectVersionAsync(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.ResourcePack,
            VersionId = "v1",
            FileName = "pack.zip",
            PrimaryDownloadUrl = "https://download.test/pack.zip",
            FallbackDownloadUrls = ["https://download.test/fallback.zip"]
        }, TempRoot, cancellation.Token);
        await stream.BlockingReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Single(handler.Requests);
        Assert.False(File.Exists(Path.Combine(TempRoot, "pack.zip")));
        Assert.Empty(Directory.GetFiles(TempRoot, "*.download"));
    }

    [Fact]
    public async Task InterruptedResponseBodyDoesNotPublishPartialFile()
    {
        var service = CreateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new InterruptingDownloadStream())
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadProjectVersionAsync(new ResourceProjectVersion
            {
                Kind = ResourceProjectKind.ResourcePack,
                VersionId = "v1",
                FileName = "pack.zip",
                PrimaryDownloadUrl = "https://download.test/pack.zip"
            }, TempRoot));

        Assert.False(File.Exists(Path.Combine(TempRoot, "pack.zip")));
        Assert.Empty(Directory.GetFiles(TempRoot, "*.download"));
    }

    [Fact]
    public async Task HttpClientTimeoutFallsBackAndPublishesVerifiedFile()
    {
        var handler = new TimeoutThenFallbackHandler();
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var service = new ResourceCatalogService(
            httpClient,
            curseForgeApiKeyResolver: new StubKeyResolver(null));

        var path = await service.DownloadProjectVersionAsync(new ResourceProjectVersion
        {
            Kind = ResourceProjectKind.ResourcePack,
            VersionId = "v1",
            FileName = "pack.zip",
            PrimaryDownloadUrl = "https://download.test/primary.zip",
            FallbackDownloadUrls = ["https://download.test/fallback.zip"],
            ExpectedFileSize = Encoding.UTF8.GetByteCount("fallback"),
            FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "fallback")]
        }, TempRoot);

        Assert.Equal("fallback", await File.ReadAllTextAsync(path));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Empty(Directory.GetFiles(TempRoot, "*.download"));
    }

    [Theory]
    [InlineData("expected", true)]
    [InlineData("modified", false)]
    [InlineData("short", false)]
    public async Task ExistingDownloadMustMatchLengthAndHash(string content, bool expectedExists)
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(Path.Combine(TempRoot, "mod.jar"), content);
        var service = CreateService(new StubHandler(_ => throw new InvalidOperationException("Download must not start.")));
        var version = new ResourceProjectVersion
        {
            VersionId = "v1",
            FileName = "mod.jar",
            ExpectedFileSize = Encoding.UTF8.GetByteCount("expected"),
            FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "expected")]
        };

        var exists = await service.ProjectVersionDownloadExistsAsync(version, TempRoot);

        Assert.Equal(expectedExists, exists);
    }

    [Fact]
    public async Task ExistingInstallMustMatchLengthAndHash()
    {
        var modsDirectory = Path.Combine(TempRoot, "mods");
        Directory.CreateDirectory(modsDirectory);
        await File.WriteAllTextAsync(Path.Combine(modsDirectory, "mod.jar"), "modified");
        var service = CreateService(new StubHandler(_ => throw new InvalidOperationException("Download must not start.")));

        var exists = await service.ProjectVersionInstallExistsAsync(
            new ResourceProjectVersion
            {
                VersionId = "v1",
                FileName = "mod.jar",
                ExpectedFileSize = Encoding.UTF8.GetByteCount("modified"),
                FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "expected")]
            },
            new GameInstance { Id = "instance", InstanceDirectory = TempRoot });

        Assert.False(exists);
    }

    [Fact]
    public async Task TemporaryFileCreationFailureDoesNotPublishTarget()
    {
        var downloadDirectory = Path.Combine(TempRoot, "download");
        var handler = new StubHandler(_ =>
        {
            Directory.Delete(downloadDirectory);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("pack") };
        });
        var service = CreateService(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadProjectVersionAsync(new ResourceProjectVersion
            {
                Kind = ResourceProjectKind.ResourcePack,
                VersionId = "v1",
                FileName = "pack.zip",
                PrimaryDownloadUrl = "https://download.test/pack.zip",
                ExpectedFileSize = 4
            }, downloadDirectory));

        Assert.False(File.Exists(Path.Combine(downloadDirectory, "pack.zip")));
    }

    [Fact]
    public async Task ModInstallRejectsModsReparsePointBeforeNetworkOrExternalWriteWhenSupported()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instance-with-linked-mods");
        var modsDirectory = Path.Combine(instanceDirectory, "mods");
        var externalDirectory = Path.Combine(TempRoot, "external-mods");
        Directory.CreateDirectory(instanceDirectory);
        Directory.CreateDirectory(externalDirectory);
        var externalFile = Path.Combine(externalDirectory, "mod.jar");
        await File.WriteAllTextAsync(externalFile, "external-original");
        try
        {
            Directory.CreateSymbolicLink(modsDirectory, externalDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("replacement") });
        var service = CreateService(handler);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallProjectVersionAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Mod,
                    VersionId = "v1",
                    FileName = "mod.jar",
                    PrimaryDownloadUrl = "https://download.test/mod.jar",
                    ExpectedFileSize = Encoding.UTF8.GetByteCount("replacement"),
                    FileHashes = [CreateHash(ResourceFileHashAlgorithm.Sha512, "replacement")]
                },
                new GameInstance { Id = "instance", InstanceDirectory = instanceDirectory }));

            Assert.Empty(handler.Requests);
            Assert.Equal("external-original", await File.ReadAllTextAsync(externalFile));
        }
        finally
        {
            if (Directory.Exists(modsDirectory))
                Directory.Delete(modsDirectory, recursive: false);
        }
    }

    private static ResourceFileHash CreateHash(ResourceFileHashAlgorithm algorithm, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = algorithm switch
        {
            ResourceFileHashAlgorithm.Sha512 => SHA512.HashData(bytes),
            ResourceFileHashAlgorithm.Sha1 => SHA1.HashData(bytes),
            ResourceFileHashAlgorithm.Md5 => MD5.HashData(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
        return new ResourceFileHash(algorithm, Convert.ToHexString(hash));
    }

    private static ResourceCatalogService CreateService(HttpMessageHandler handler, string? key = null) =>
        new(new HttpClient(handler), curseForgeApiKeyResolver: new StubKeyResolver(key));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response(request));
        }
    }

    private sealed class StubKeyResolver(string? key) : ICurseForgeApiKeyResolver
    {
        public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(key);
    }

    private sealed class CancelableDownloadStream : Stream
    {
        private int readCount;

        public TaskCompletionSource BlockingReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref readCount) == 1)
            {
                "partial"u8.CopyTo(buffer.Span);
                return 7;
            }
            BlockingReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class InterruptingDownloadStream : Stream
    {
        private int readCount;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref readCount) == 1)
            {
                "partial"u8.CopyTo(buffer.Span);
                return ValueTask.FromResult(7);
            }
            return ValueTask.FromException<int>(new IOException("The response body was interrupted."));
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TimeoutThenFallbackHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath.Contains("primary", StringComparison.Ordinal))
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fallback") };
        }
    }

    private sealed class ConcurrentProviderSearchHandler : HttpMessageHandler
    {
        private int startedCount;

        public TaskCompletionSource AllProvidersStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartedCount => Volatile.Read(ref startedCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref startedCount) == 2)
                AllProvidersStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return request.RequestUri!.Host == "api.modrinth.com"
                ? Json("""{"hits":[{"project_id":"m","slug":"modrinth","title":"Modrinth","description":"","downloads":50}]}""")
                : Json("""{"data":[{"id":9,"name":"CurseForge","slug":"curseforge","summary":"","downloadCount":120,"links":null,"logo":null}]}""");
        }
    }

    private sealed class BlockingCurseForgeSearchHandler : HttpMessageHandler
    {
        private int activeRequests;
        private int maxActiveRequests;
        private int requestCount;

        public TaskCompletionSource FirstWaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ActiveRequests => Volatile.Read(ref activeRequests);

        public int MaxActiveRequests => Volatile.Read(ref maxActiveRequests);

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            var active = Interlocked.Increment(ref activeRequests);
            UpdateMaximum(ref maxActiveRequests, active);
            if (active == 4)
                FirstWaveStarted.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return Json("""{"data":[{"id":9,"name":"Project","slug":"project","summary":"","downloadCount":120,"links":null,"logo":null}]}""");
            }
            finally
            {
                Interlocked.Decrement(ref activeRequests);
            }
        }

        private static void UpdateMaximum(ref int maximum, int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximum);
                if (candidate <= current || Interlocked.CompareExchange(ref maximum, candidate, current) == current)
                    return;
            }
        }
    }

    private sealed class OutOfOrderCurseForgeSearchHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource secondCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Query.Contains("gameVersion=first", StringComparison.Ordinal))
            {
                await secondCompleted.Task.WaitAsync(cancellationToken);
                return Project("First version project");
            }

            secondCompleted.TrySetResult();
            return Project("Second version project");
        }

        private static HttpResponseMessage Project(string title) => Json(
            $$"""{"data":[{"id":9,"name":"{{title}}","slug":"project","summary":"","downloadCount":120,"links":null,"logo":null}]}""");
    }
}
