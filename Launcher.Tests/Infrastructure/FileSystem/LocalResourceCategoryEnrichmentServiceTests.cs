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

}
