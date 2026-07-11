/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
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
            _ => """[{"id":"v1","name":"Main 1.0","version_number":"1.0","version_type":"release","date_published":"2024-01-01T00:00:00Z","downloads":1,"game_versions":["1.20.1"],"loaders":["fabric"],"dependencies":[{"project_id":"dep","version_id":"dep-v1","dependency_type":"required"}],"files":[{"filename":"main.jar","url":"https://download/main.jar","primary":true}]}]"""
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
            PrimaryDownloadUrl = "https://download/missing.jar",
            FallbackDownloadUrls = ["https://download/fallback.jar"]
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
            PrimaryDownloadUrl = "https://download/mod.jar"
        }, instance);

        Assert.Equal(Path.Combine(TempRoot, "mods", "mod.jar"), path);
        Assert.Equal("jar", await File.ReadAllTextAsync(path));
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
