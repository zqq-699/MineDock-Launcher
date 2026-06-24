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
            {"hits":[{"project_id":"p1","slug":"sodium","title":"Sodium","description":"Fast","icon_url":null,"downloads":42,"versions":["1.20.1","1.19.4"],"categories":["fabric","optimization","forge"]}],"total_hits":45}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            Query = "sodium",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            Source = ResourceProjectSource.Modrinth,
            Offset = 20,
            PageSize = 20
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(["1.20.1", "1.19.4"], project.SupportedMinecraftVersions);
        Assert.Equal(["fabric", "forge"], project.SupportedLoaders);
        Assert.True(result.HasMore);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.modrinth.com", request.RequestUri!.Host);
        Assert.Contains("limit=20", request.RequestUri.Query);
        Assert.Contains("offset=20", request.RequestUri.Query);
        Assert.Contains("index=downloads", request.RequestUri.Query);
        Assert.Contains("query=sodium", request.RequestUri.Query);
        Assert.Contains("project_type%3Amod", request.RequestUri.Query);
        Assert.Contains("versions%3A1.20.1", request.RequestUri.Query);
        Assert.Contains("categories%3Afabric", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchModsAddsModrinthMultiVersionFacetGroup()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"sodium","title":"Sodium","description":"Fast","icon_url":null,"downloads":42,"versions":["1.20.1"],"categories":["fabric"]}],"total_hits":1}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            MinecraftVersions = ["1.20.6", "1.20.1", "1.20"],
            Source = ResourceProjectSource.Modrinth
        });

        var request = Assert.Single(handler.Requests);
        Assert.Contains("versions%3A1.20.6", request.RequestUri!.Query);
        Assert.Contains("versions%3A1.20.1", request.RequestUri.Query);
        Assert.Contains("versions%3A1.20", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchModsAddsCurseForgeDownloadSortParameters()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"JourneyMap","slug":"journeymap","summary":"Map","downloadCount":120,"links":{"websiteUrl":"https://www.curseforge.com/minecraft/mc-mods/journeymap"},"logo":null,"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":1},{"gameVersion":"1.19.4","modLoader":4}],"gameVersionLatestFiles":[{"gameVersion":"1.18.2","modLoader":6}]}],"pagination":{"index":20,"pageSize":20,"resultCount":1,"totalCount":21}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            Query = "map",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Forge,
            Source = ResourceProjectSource.CurseForge,
            Offset = 20,
            PageSize = 20
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(["1.20.1", "1.19.4", "1.18.2"], project.SupportedMinecraftVersions);
        Assert.Equal(["forge", "fabric", "neoforge"], project.SupportedLoaders);
        Assert.False(result.HasMore);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.curseforge.com", request.RequestUri!.Host);
        Assert.True(request.Headers.TryGetValues("x-api-key", out var values));
        Assert.Equal("test-key", Assert.Single(values));
        Assert.Contains("gameId=432", request.RequestUri.Query);
        Assert.Contains("classId=6", request.RequestUri.Query);
        Assert.Contains("sortField=6", request.RequestUri.Query);
        Assert.Contains("sortOrder=desc", request.RequestUri.Query);
        Assert.Contains("pageSize=20", request.RequestUri.Query);
        Assert.Contains("index=20", request.RequestUri.Query);
        Assert.Contains("searchFilter=map", request.RequestUri.Query);
        Assert.Contains("gameVersion=1.20.1", request.RequestUri.Query);
        Assert.Contains("modLoaderType=1", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchModsQueriesCurseForgeOncePerMinecraftVersionAndDeduplicates()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"JourneyMap","slug":"journeymap","summary":"Map","downloadCount":120,"links":null,"logo":null}],"pagination":{"index":0,"pageSize":20,"resultCount":1,"totalCount":1}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            MinecraftVersions = ["1.20.6", "1.20.1"],
            Source = ResourceProjectSource.CurseForge
        });

        Assert.Single(result.Projects);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("api.curseforge.com", request.RequestUri!.Host));
        Assert.Contains(handler.Requests, request => request.RequestUri!.Query.Contains("gameVersion=1.20.6", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.RequestUri!.Query.Contains("gameVersion=1.20.1", StringComparison.Ordinal));
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

    [Fact]
    public async Task GetProjectVersionsQueriesModrinthWithMinecraftVersionAndLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            curseForgeResponse: """{"data":[]}""",
            modrinthVersionResponse:
            """
            [{"id":"v1","name":"Sodium 1.0","version_number":"1.0.0","version_type":"release","date_published":"2024-01-02T00:00:00Z","downloads":15,"game_versions":["1.18.2"],"loaders":["fabric"],"files":[{"filename":"sodium.jar","url":"https://downloads.example.test/sodium.jar","primary":true}]}]
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "sodium",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric
        });

        var version = Assert.Single(result.Versions);
        Assert.Equal("Sodium 1.0", version.Name);
        Assert.Equal("sodium.jar", version.FileName);
        Assert.Equal("https://downloads.example.test/sodium.jar", version.PrimaryDownloadUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.modrinth.com", request.RequestUri!.Host);
        Assert.Contains("/project/sodium/version", request.RequestUri.AbsolutePath);
        Assert.Contains("game_versions=", request.RequestUri.Query);
        Assert.Contains("1.18.2", Uri.UnescapeDataString(request.RequestUri.Query));
        Assert.Contains("fabric", Uri.UnescapeDataString(request.RequestUri.Query));
    }

    [Fact]
    public async Task GetProjectVersionsQueriesCurseForgeWithMinecraftVersionAndLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":101,"displayName":"JourneyMap 1.0","fileName":"journeymap.jar","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.18.2","Fabric"],"sortableGameVersions":[{"gameVersion":"1.18.2","modLoader":4}]}],"pagination":{"index":0,"pageSize":50,"resultCount":1,"totalCount":1}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            MinecraftVersion = "1.18.2",
            Loader = LoaderKind.Fabric,
            PageSize = 50
        });

        var version = Assert.Single(result.Versions);
        Assert.Equal("JourneyMap 1.0", version.Name);
        Assert.Equal("release", version.VersionType);
        Assert.Equal(["fabric"], version.Loaders);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.curseforge.com", request.RequestUri!.Host);
        Assert.Contains("/mods/1234/files", request.RequestUri.AbsolutePath);
        Assert.Contains("gameVersion=1.18.2", request.RequestUri.Query);
        Assert.Contains("modLoaderType=4", request.RequestUri.Query);
        Assert.Contains("pageSize=50", request.RequestUri.Query);
    }

    [Fact]
    public async Task GetProjectVersionsQueriesModrinthWithoutFiltersWhenIncludingAllVersions()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            modrinthVersionResponse:
            """
            [{"id":"v1","name":"Sodium 1.0","version_number":"1.0.0","version_type":"release","date_published":"2024-01-02T00:00:00Z","downloads":15,"game_versions":["1.18.2"],"loaders":["fabric"],"files":[{"filename":"sodium.jar","url":"https://downloads.example.test/sodium.jar","primary":true}]}]
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "sodium",
            IncludeAllVersions = true
        });

        Assert.Single(result.Versions);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.modrinth.com", request.RequestUri!.Host);
        Assert.Contains("/project/sodium/version", request.RequestUri.AbsolutePath);
        Assert.DoesNotContain("game_versions", request.RequestUri.Query);
        Assert.DoesNotContain("loaders", request.RequestUri.Query);
    }

    [Fact]
    public async Task GetProjectVersionsQueriesCurseForgeWithoutFiltersWhenIncludingAllVersions()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":101,"displayName":"JourneyMap 1.0","fileName":"journeymap.jar","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.18.2","Fabric"],"sortableGameVersions":[{"gameVersion":"1.18.2","modLoader":4}]}],"pagination":{"index":0,"pageSize":50,"resultCount":1,"totalCount":1}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            IncludeAllVersions = true,
            PageSize = 50
        });

        Assert.Single(result.Versions);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.curseforge.com", request.RequestUri!.Host);
        Assert.Contains("/mods/1234/files", request.RequestUri.AbsolutePath);
        Assert.DoesNotContain("gameVersion=", request.RequestUri.Query);
        Assert.DoesNotContain("modLoaderType=", request.RequestUri.Query);
        Assert.Contains("pageSize=50", request.RequestUri.Query);
    }

    [Fact]
    public async Task InstallProjectVersionDownloadsFileToInstanceModsDirectory()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/mod.jar"] = "jar-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-resource-install-{Guid.NewGuid():N}");
        try
        {
            var installedPath = await service.InstallProjectVersionAsync(
                new ResourceProjectVersion
                {
                    VersionId = "version-1",
                    FileName = "mod.jar",
                    PrimaryDownloadUrl = "https://downloads.example.test/mod.jar"
                },
                new GameInstance
                {
                    Id = "instance",
                    InstanceDirectory = instanceDirectory
                });

            Assert.Equal(Path.Combine(instanceDirectory, "mods", "mod.jar"), installedPath);
            Assert.True(File.Exists(installedPath));
            Assert.Equal("jar-content", await File.ReadAllTextAsync(installedPath));
        }
        finally
        {
            if (Directory.Exists(instanceDirectory))
                Directory.Delete(instanceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadProjectVersionDownloadsFileToTargetDirectory()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/mod.jar"] = "jar-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"launcher-resource-download-{Guid.NewGuid():N}");
        try
        {
            var downloadedPath = await service.DownloadProjectVersionAsync(
                new ResourceProjectVersion
                {
                    VersionId = "version-1",
                    FileName = "mod.jar",
                    PrimaryDownloadUrl = "https://downloads.example.test/mod.jar"
                },
                targetDirectory);

            Assert.Equal(Path.Combine(targetDirectory, "mod.jar"), downloadedPath);
            Assert.True(File.Exists(downloadedPath));
            Assert.Equal("jar-content", await File.ReadAllTextAsync(downloadedPath));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadProjectVersionTriesFallbackUrlWhenPrimaryFails()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/fallback.jar"] = "fallback-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"launcher-resource-download-{Guid.NewGuid():N}");
        try
        {
            var downloadedPath = await service.DownloadProjectVersionAsync(
                new ResourceProjectVersion
                {
                    VersionId = "version-1",
                    FileName = "mod.jar",
                    PrimaryDownloadUrl = "https://downloads.example.test/missing.jar",
                    FallbackDownloadUrls = ["https://downloads.example.test/fallback.jar"]
                },
                targetDirectory);

            Assert.Equal(Path.Combine(targetDirectory, "mod.jar"), downloadedPath);
            Assert.Equal("fallback-content", await File.ReadAllTextAsync(downloadedPath));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectVersionDownloadExistsReturnsTrueWhenTargetFileExists()
    {
        var handler = new ResourceCatalogHandler("""{"hits":[]}""");
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"launcher-resource-exists-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(targetDirectory);
            await File.WriteAllTextAsync(Path.Combine(targetDirectory, "mod.jar"), "existing");

            var exists = await service.ProjectVersionDownloadExistsAsync(
                new ResourceProjectVersion
                {
                    VersionId = "version-1",
                    FileName = "mod.jar"
                },
                targetDirectory);

            Assert.True(exists);
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectVersionInstallExistsReturnsTrueWhenInstanceModFileExists()
    {
        var handler = new ResourceCatalogHandler("""{"hits":[]}""");
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-resource-install-exists-{Guid.NewGuid():N}");
        try
        {
            var modsDirectory = Path.Combine(instanceDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);
            await File.WriteAllTextAsync(Path.Combine(modsDirectory, "mod.jar"), "existing");

            var exists = await service.ProjectVersionInstallExistsAsync(
                new ResourceProjectVersion
                {
                    VersionId = "version-1",
                    FileName = "mod.jar"
                },
                new GameInstance
                {
                    Id = "instance",
                    InstanceDirectory = instanceDirectory
                });

            Assert.True(exists);
        }
        finally
        {
            if (Directory.Exists(instanceDirectory))
                Directory.Delete(instanceDirectory, recursive: true);
        }
    }

    private sealed class ResourceCatalogHandler : HttpMessageHandler
    {
        private readonly string modrinthResponse;
        private readonly string curseForgeResponse;
        private readonly string modrinthVersionResponse;
        private readonly IReadOnlyDictionary<string, string> downloadResponses;

        public ResourceCatalogHandler(
            string modrinthResponse,
            string? curseForgeResponse = null,
            string? modrinthVersionResponse = null,
            IReadOnlyDictionary<string, string>? downloadResponses = null)
        {
            this.modrinthResponse = modrinthResponse;
            this.curseForgeResponse = curseForgeResponse ?? """{"data":[]}""";
            this.modrinthVersionResponse = modrinthVersionResponse ?? "[]";
            this.downloadResponses = downloadResponses ?? new Dictionary<string, string>();
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            if (downloadResponses.TryGetValue(request.RequestUri!.ToString(), out var downloadBody))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(downloadBody)
                });
            }

            if (request.RequestUri.Host == "downloads.example.test")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var body = request.RequestUri?.Host switch
            {
                "api.modrinth.com" when request.RequestUri.AbsolutePath.Contains("/version", StringComparison.Ordinal)
                    => modrinthVersionResponse,
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
