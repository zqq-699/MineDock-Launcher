/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class LocalResourceCategoryEnrichmentServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ExactModrinthFileMatchMapsCategoriesForRequestedResourceKind()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "shader.zip");
        var duplicatePath = Path.Combine(TempRoot, "shader-copy.zip");
        var bytes = Encoding.UTF8.GetBytes("recognized shader archive");
        File.WriteAllBytes(path, bytes);
        File.WriteAllBytes(duplicatePath, bytes);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        using var httpClient = new HttpClient(new ModrinthMatchHandler(sha1));
        var service = new LocalResourceCategoryEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);

        var result = await service.ResolveCategoriesAsync(
        [
            new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack),
            new LocalResourceCategoryCandidate(duplicatePath, ResourceProjectKind.ShaderPack)
        ]);

        Assert.Equal(2, result.Count);
        Assert.All(result.Values, categories => Assert.Equal(
            [ResourceProjectCategory.Fantasy, ResourceProjectCategory.Realistic],
            categories));
    }

    [Fact]
    public async Task DirectoryResourcesAreNotSubmittedForRemoteFileMatching()
    {
        var path = Path.Combine(TempRoot, "folder-pack");
        Directory.CreateDirectory(path);
        using var httpClient = new HttpClient(new RejectingHandler());
        var service = new LocalResourceCategoryEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);

        var result = await service.ResolveCategoriesAsync(
            [new LocalResourceCategoryCandidate(path, ResourceProjectKind.ResourcePack)]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LargeLocalListsAreSplitIntoBoundedProviderRequests()
    {
        Directory.CreateDirectory(TempRoot);
        var candidates = Enumerable.Range(0, LocalResourceCategoryEnrichmentService.ProviderBatchSize + 1)
            .Select(index =>
            {
                var path = Path.Combine(TempRoot, $"mod-{index}.jar");
                File.WriteAllText(path, $"mod content {index}");
                return new LocalResourceCategoryCandidate(path, ResourceProjectKind.Mod);
            })
            .ToArray();
        var handler = new BatchingModrinthHandler();
        using var httpClient = new HttpClient(handler);
        var service = new LocalResourceCategoryEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);

        var result = await service.ResolveCategoriesAsync(candidates);

        Assert.Equal(candidates.Length, result.Count);
        Assert.Equal(2, handler.VersionFileRequestCount);
        Assert.Equal(LocalResourceCategoryEnrichmentService.ProviderBatchSize, handler.MaximumHashCount);
        Assert.All(result.Values, categories => Assert.Equal([ResourceProjectCategory.Optimization], categories));
    }

    [Fact]
    public async Task PersistedCategoriesAreAvailableAfterServiceRestartWithoutNetworkRequests()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "cached-mod.jar");
        var bytes = Encoding.UTF8.GetBytes("persisted recognized mod");
        File.WriteAllBytes(path, bytes);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        var pathProvider = new LauncherPathProvider(TempRoot);

        using (var firstClient = new HttpClient(new ModrinthMatchHandler(sha1)))
        {
            var firstService = new LocalResourceCategoryEnrichmentService(
                pathProvider,
                firstClient,
                logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);
            var first = await firstService.ResolveCategoriesAsync(
                [new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack)]);
            Assert.Equal(
                [ResourceProjectCategory.Fantasy, ResourceProjectCategory.Realistic],
                Assert.Single(first).Value);
        }

        using var secondClient = new HttpClient(new RejectingHandler());
        var restartedService = new LocalResourceCategoryEnrichmentService(
            pathProvider,
            secondClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);
        var candidate = new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack);

        var cached = await restartedService.ResolveCachedCategoriesAsync([candidate]);
        var resolved = await restartedService.ResolveCategoriesAsync([candidate]);

        Assert.Equal(
            [ResourceProjectCategory.Fantasy, ResourceProjectCategory.Realistic],
            Assert.Single(cached).Value);
        Assert.Equal(cached.Single().Value, Assert.Single(resolved).Value);
        Assert.True(File.Exists(Path.Combine(
            pathProvider.DefaultDataDirectory,
            "cache",
            "resources",
            "local-categories",
            "index.json")));
    }

    [Fact]
    public async Task MatchedShaderPackUsesPersistedWebsiteIconMetadataAfterRestart()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "cached-shader.zip");
        var bytes = Encoding.UTF8.GetBytes("recognized shader with website icon");
        File.WriteAllBytes(path, bytes);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        var pathProvider = new LauncherPathProvider(TempRoot);
        var firstThumbnailService = new RecordingThumbnailService();

        using (var firstClient = new HttpClient(new ModrinthMatchHandler(sha1)))
        {
            var firstService = new LocalResourceCategoryEnrichmentService(
                pathProvider,
                firstClient,
                logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance,
                thumbnailService: firstThumbnailService);

            var first = await firstService.ResolveMetadataAsync(
                [new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack)]);

            Assert.Equal("file:///cached-shader-icon.png", Assert.Single(first).Value.IconSource);
            Assert.Equal(1, firstThumbnailService.CreateCount);
            Assert.Equal(ResourceProjectSource.Modrinth, firstThumbnailService.LastProject?.Source);
            Assert.Equal("shader-project", firstThumbnailService.LastProject?.ProjectId);
            Assert.Equal("https://cdn.example/shader.png", firstThumbnailService.LastProject?.IconUrl);
            Assert.Equal(
                new ResourceProjectReference(
                    ResourceProjectKind.ShaderPack,
                    ResourceProjectSource.Modrinth,
                    "shader-project"),
                Assert.Single(first).Value.ProjectReference);
        }

        var restartedThumbnailService = new RecordingThumbnailService("file:///cached-shader-icon.png");
        using var secondClient = new HttpClient(new RejectingHandler());
        var restartedService = new LocalResourceCategoryEnrichmentService(
            pathProvider,
            secondClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance,
            thumbnailService: restartedThumbnailService);
        var candidate = new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack);

        var cached = await restartedService.ResolveCachedMetadataAsync([candidate]);
        var resolved = await restartedService.ResolveMetadataAsync([candidate]);

        Assert.Equal("file:///cached-shader-icon.png", Assert.Single(cached).Value.IconSource);
        Assert.Equal("file:///cached-shader-icon.png", Assert.Single(resolved).Value.IconSource);
        Assert.Equal("shader-project", Assert.Single(cached).Value.ProjectReference?.ProjectId);
        Assert.Equal(0, restartedThumbnailService.CreateCount);
        Assert.Equal("shader-project", restartedThumbnailService.LastProject?.ProjectId);
    }

    private sealed class ModrinthMatchHandler(string sha1) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/v2/version_files" => $"{{\"{sha1}\":{{\"project_id\":\"shader-project\"}}}}",
                "/v2/projects" => """[{"id":"shader-project","icon_url":"https://cdn.example/shader.png","categories":["fabric","fantasy","realistic","fantasy"]}]""",
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Directory resources must not trigger remote matching.");
    }

    private sealed class BatchingModrinthHandler : HttpMessageHandler
    {
        public int VersionFileRequestCount { get; private set; }

        public int MaximumHashCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string json;
            if (request.RequestUri!.AbsolutePath == "/v2/version_files")
            {
                VersionFileRequestCount++;
                using var document = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken));
                var hashes = document.RootElement.GetProperty("hashes")
                    .EnumerateArray()
                    .Select(element => element.GetString()!)
                    .ToArray();
                MaximumHashCount = Math.Max(MaximumHashCount, hashes.Length);
                json = JsonSerializer.Serialize(hashes.ToDictionary(
                    hash => hash,
                    _ => new Dictionary<string, string> { ["project_id"] = "project" }));
            }
            else if (request.RequestUri.AbsolutePath == "/v2/projects")
            {
                json = """[{"id":"project","categories":["optimization"]}]""";
            }
            else
            {
                throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class RecordingThumbnailService(string? cachedSource = null) : IResourceThumbnailService
    {
        private string? source = cachedSource;

        public int CreateCount { get; private set; }

        public ResourceProject? LastProject { get; private set; }

        public string? TryGetCachedThumbnailSource(ResourceProject project)
        {
            LastProject = project;
            return source;
        }

        public Task<string?> GetOrCreateThumbnailSourceAsync(
            ResourceProject project,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastProject = project;
            CreateCount++;
            source = "file:///cached-shader-icon.png";
            return Task.FromResult<string?>(source);
        }
    }
}
