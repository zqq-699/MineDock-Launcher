using System.Net;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.Resources;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class ResourceCatalogServiceTests
{
    [Fact]
    public async Task SearchModsAddsModrinthDownloadSortAndFacets()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"sodium","title":"Sodium","description":"Fast","icon_url":null,"downloads":42,"versions":["1.20.1","1.19.4"],"categories":["fabric","optimization","forge"]}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            Query = "sodium",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            Source = ResourceProjectSource.Modrinth
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(["1.20.1", "1.19.4"], project.SupportedMinecraftVersions);
        Assert.Equal(["fabric", "forge"], project.SupportedLoaders);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.modrinth.com", request.RequestUri!.Host);
        Assert.Contains("limit=20", request.RequestUri.Query);
        Assert.Contains("index=downloads", request.RequestUri.Query);
        Assert.Contains("query=sodium", request.RequestUri.Query);
        Assert.Contains("project_type%3Amod", request.RequestUri.Query);
        Assert.Contains("versions%3A1.20.1", request.RequestUri.Query);
        Assert.Contains("categories%3Afabric", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchModsAddsCurseForgeDownloadSortParameters()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"JourneyMap","slug":"journeymap","summary":"Map","downloadCount":120,"links":{"websiteUrl":"https://www.curseforge.com/minecraft/mc-mods/journeymap"},"logo":null,"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":1},{"gameVersion":"1.19.4","modLoader":4}],"gameVersionLatestFiles":[{"gameVersion":"1.18.2","modLoader":6}]}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            Query = "map",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Forge,
            Source = ResourceProjectSource.CurseForge
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(["1.20.1", "1.19.4", "1.18.2"], project.SupportedMinecraftVersions);
        Assert.Equal(["forge", "fabric", "neoforge"], project.SupportedLoaders);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.curseforge.com", request.RequestUri!.Host);
        Assert.True(request.Headers.TryGetValues("x-api-key", out var values));
        Assert.Equal("test-key", Assert.Single(values));
        Assert.Contains("gameId=432", request.RequestUri.Query);
        Assert.Contains("classId=6", request.RequestUri.Query);
        Assert.Contains("sortField=6", request.RequestUri.Query);
        Assert.Contains("sortOrder=desc", request.RequestUri.Query);
        Assert.Contains("pageSize=20", request.RequestUri.Query);
        Assert.Contains("searchFilter=map", request.RequestUri.Query);
        Assert.Contains("gameVersion=1.20.1", request.RequestUri.Query);
        Assert.Contains("modLoaderType=1", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchModsMergesSourcesByDownloadsDescending()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"modrinth-mod","title":"Modrinth Mod","description":"Fast","icon_url":null,"downloads":50}]}
            """,
            """
            {"data":[{"id":9,"name":"CurseForge Mod","slug":"curseforge-mod","summary":"Popular","downloadCount":120,"links":null,"logo":null}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest());

        Assert.Equal(["CurseForge Mod", "Modrinth Mod"], result.Projects.Select(project => project.Title));
        Assert.Equal([120, 50], result.Projects.Select(project => project.Downloads));
    }

    [Fact]
    public async Task SearchModsSkipsCurseForgeWhenApiKeyMissing()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"sodium","title":"Sodium","description":"Fast","icon_url":null,"downloads":42}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest());

        Assert.True(result.IsCurseForgeUnavailable);
        Assert.True(result.IsCurseForgeApiKeyMissing);
        Assert.Single(result.Projects);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.Host == "api.curseforge.com");
    }

    private sealed class ResourceCatalogHandler : HttpMessageHandler
    {
        private readonly string modrinthResponse;
        private readonly string curseForgeResponse;

        public ResourceCatalogHandler(string modrinthResponse, string? curseForgeResponse = null)
        {
            this.modrinthResponse = modrinthResponse;
            this.curseForgeResponse = curseForgeResponse ?? """{"data":[]}""";
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            var body = request.RequestUri?.Host switch
            {
                "api.modrinth.com" => modrinthResponse,
                "api.curseforge.com" => curseForgeResponse,
                _ => throw new InvalidOperationException($"Unexpected host: {request.RequestUri?.Host}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return clone;
        }
    }

    private sealed class StubCurseForgeApiKeyResolver(string? apiKey) : ICurseForgeApiKeyResolver
    {
        public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(apiKey);
        }
    }
}
