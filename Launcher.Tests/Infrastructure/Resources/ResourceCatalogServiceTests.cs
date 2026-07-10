/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.IO.Compression;
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
            Category = ResourceProjectCategory.Optimization,
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
        Assert.Contains("categories%3Aoptimization", request.RequestUri.Query);
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
    public async Task SearchResourcePacksAddsModrinthResourcePackFacet()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"fresh-animations","title":"Fresh Animations","description":"Animated mobs","icon_url":null,"downloads":42,"versions":["1.20.1"],"categories":["vanilla-like"]}],"total_hits":1}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.ResourcePack,
            Source = ResourceProjectSource.Modrinth,
            Category = ResourceProjectCategory.VanillaLike
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(ResourceProjectKind.ResourcePack, project.Kind);
        Assert.Empty(project.SupportedLoaders);
        Assert.Equal("https://modrinth.com/resourcepack/fresh-animations", project.ProjectUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("project_type%3Aresourcepack", request.RequestUri!.Query);
        Assert.Contains("categories%3Avanilla-like", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchShaderPacksAddsModrinthShaderFacet()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"complementary","title":"Complementary","description":"Nice shaders","icon_url":null,"downloads":42,"versions":["1.20.1"],"categories":["fantasy"]}],"total_hits":1}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.ShaderPack,
            Source = ResourceProjectSource.Modrinth,
            Category = ResourceProjectCategory.Fantasy
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(ResourceProjectKind.ShaderPack, project.Kind);
        Assert.Empty(project.SupportedLoaders);
        Assert.Equal("https://modrinth.com/shader/complementary", project.ProjectUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("project_type%3Ashader", request.RequestUri!.Query);
        Assert.Contains("categories%3Afantasy", request.RequestUri.Query);
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
    public async Task SearchResourcePacksUsesCurseForgeResourcePackClassIdWithoutLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"Faithful","slug":"faithful","summary":"Clean textures","downloadCount":120,"links":{"websiteUrl":"https://www.curseforge.com/minecraft/texture-packs/faithful"},"logo":null,"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":1}],"gameVersionLatestFiles":[]}],"pagination":{"index":0,"pageSize":20,"resultCount":1,"totalCount":1}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.ResourcePack,
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            Source = ResourceProjectSource.CurseForge
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(ResourceProjectKind.ResourcePack, project.Kind);
        Assert.Empty(project.SupportedLoaders);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("classId=12", request.RequestUri!.Query);
        Assert.DoesNotContain("modLoaderType=", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchShaderPacksUsesCurseForgeShaderClassIdWithoutLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"Complementary","slug":"complementary","summary":"Nice shaders","downloadCount":120,"links":null,"logo":null,"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":4}],"gameVersionLatestFiles":[]}],"pagination":{"index":0,"pageSize":20,"resultCount":1,"totalCount":1}}
            """,
            curseForgeCategoriesResponse:
            """
            {"data":[{"id":6553,"name":"Fantasy","slug":"fantasy"}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.ShaderPack,
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            Source = ResourceProjectSource.CurseForge,
            Category = ResourceProjectCategory.Fantasy
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(ResourceProjectKind.ShaderPack, project.Kind);
        Assert.Empty(project.SupportedLoaders);
        Assert.Equal("https://www.curseforge.com/minecraft/shaders/complementary", project.ProjectUrl);
        Assert.Equal(2, handler.Requests.Count);

        var categoriesRequest = handler.Requests[0];
        Assert.Contains("/categories", categoriesRequest.RequestUri!.AbsolutePath);
        Assert.Contains("classId=6552", categoriesRequest.RequestUri.Query);

        var searchRequest = handler.Requests[1];
        Assert.Contains("classId=6552", searchRequest.RequestUri!.Query);
        Assert.Contains("categoryId=6553", searchRequest.RequestUri.Query);
        Assert.DoesNotContain("modLoaderType=", searchRequest.RequestUri.Query);
    }

    [Fact]
    public async Task SearchWorldsUsesCurseForgeWorldClassIdWithoutLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"SkyBlock","slug":"skyblock","summary":"A world","downloadCount":120,"links":null,"logo":null,"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":4}],"gameVersionLatestFiles":[]}],"pagination":{"index":0,"pageSize":20,"resultCount":1,"totalCount":1}}
            """,
            curseForgeCategoriesResponse:
            """
            {"data":[{"id":17,"name":"Parkour","slug":"parkour"}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.World,
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            Source = ResourceProjectSource.CurseForge,
            Category = ResourceProjectCategory.Parkour
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(ResourceProjectKind.World, project.Kind);
        Assert.Empty(project.SupportedLoaders);
        Assert.Equal("https://www.curseforge.com/minecraft/worlds/skyblock", project.ProjectUrl);
        Assert.Equal(2, handler.Requests.Count);

        var categoriesRequest = handler.Requests[0];
        Assert.Contains("/categories", categoriesRequest.RequestUri!.AbsolutePath);
        Assert.Contains("classId=17", categoriesRequest.RequestUri.Query);

        var searchRequest = handler.Requests[1];
        Assert.Contains("classId=17", searchRequest.RequestUri!.Query);
        Assert.Contains("categoryId=17", searchRequest.RequestUri.Query);
        Assert.DoesNotContain("modLoaderType=", searchRequest.RequestUri.Query);
    }

    [Fact]
    public async Task SearchWorldsWithModrinthSourceReturnsEmptyWithoutCallingModrinth()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"not-a-world","title":"Not A World","description":"Data","icon_url":null,"downloads":42}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.World,
            Source = ResourceProjectSource.Modrinth
        });

        Assert.Empty(result.Projects);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchModpacksAddsModrinthModpackFacet()
    {
        var handler = new ResourceCatalogHandler(
            """
            {"hits":[{"project_id":"p1","slug":"pack","title":"Pack","description":"A pack","icon_url":null,"downloads":42,"versions":["1.20.1"],"categories":["fabric","quests"]}],"total_hits":1}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.Modrinth,
            Loader = LoaderKind.Fabric,
            Category = ResourceProjectCategory.Quests
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(ResourceProjectKind.Modpack, project.Kind);
        Assert.Equal(["fabric"], project.SupportedLoaders);
        Assert.Equal("https://modrinth.com/modpack/pack", project.ProjectUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.modrinth.com", request.RequestUri!.Host);
        Assert.Contains("project_type%3Amodpack", request.RequestUri.Query);
        Assert.Contains("categories%3Afabric", request.RequestUri.Query);
        Assert.Contains("categories%3Aquests", request.RequestUri.Query);
    }

    [Fact]
    public async Task SearchModpacksUsesCurseForgeModpackClassIdAndWebsitePath()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"Pack","slug":"pack","summary":"A pack","downloadCount":120,"links":null,"logo":null,"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":4}],"gameVersionLatestFiles":[]}],"pagination":{"index":0,"pageSize":20,"resultCount":1,"totalCount":1}}
            """,
            curseForgeCategoriesResponse:
            """
            {"data":[{"id":4472,"name":"Quests","slug":"quests"}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchProjectsAsync(new ResourceCatalogSearchRequest
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.CurseForge,
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            Category = ResourceProjectCategory.Quests
        });

        var project = Assert.Single(result.Projects);
        Assert.Equal(ResourceProjectKind.Modpack, project.Kind);
        Assert.Equal(["fabric"], project.SupportedLoaders);
        Assert.Equal("https://www.curseforge.com/minecraft/modpacks/pack", project.ProjectUrl);
        Assert.Equal(2, handler.Requests.Count);

        var categoriesRequest = handler.Requests[0];
        Assert.Contains("/categories", categoriesRequest.RequestUri!.AbsolutePath);
        Assert.Contains("classId=4471", categoriesRequest.RequestUri.Query);

        var searchRequest = handler.Requests[1];
        Assert.Contains("classId=4471", searchRequest.RequestUri!.Query);
        Assert.Contains("categoryId=4472", searchRequest.RequestUri.Query);
        Assert.Contains("modLoaderType=4", searchRequest.RequestUri.Query);
    }

    [Fact]
    public async Task GetModpackVersionsQueriesModrinthWithMinecraftVersionAndLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            modrinthVersionResponse:
            """
            [{"id":"v1","name":"Pack 1.0","version_number":"1.0.0","version_type":"release","date_published":"2024-01-02T00:00:00Z","downloads":15,"game_versions":["1.20.1"],"loaders":["fabric"],"files":[{"filename":"pack.mrpack","url":"https://downloads.example.test/pack.mrpack","primary":true}]}]
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric
        });

        var version = Assert.Single(result.Versions);
        Assert.Equal(ResourceProjectKind.Modpack, version.Kind);
        Assert.Equal("pack.mrpack", version.FileName);
        Assert.Equal(["fabric"], version.Loaders);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.modrinth.com", request.RequestUri!.Host);
        Assert.Contains("game_versions=", request.RequestUri.Query);
        Assert.Contains("loaders=", request.RequestUri.Query);
        Assert.Contains("fabric", Uri.UnescapeDataString(request.RequestUri.Query));
    }

    [Fact]
    public async Task GetProjectDependenciesMapsRequiredModrinthVersionDependencies()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            modrinthVersionResponse:
            """
            [
              {"id":"v1","dependencies":[
                {"project_id":"AANobbMI","dependency_type":"required"},
                {"project_id":"optional-lib","dependency_type":"optional"},
                {"project_id":"embedded-lib","dependency_type":"embedded"},
                {"file_name":"external.jar","dependency_type":"required"},
                {"project_id":"main-mod","dependency_type":"required"}
              ],"files":[{"filename":"iris.jar","url":"https://downloads.example.test/iris.jar","primary":true}]},
              {"id":"v2","dependencies":[{"project_id":"AANobbMI","dependency_type":"required"}],"files":[{"filename":"iris2.jar","url":"https://downloads.example.test/iris2.jar","primary":true}]}
            ]
            """,
            modrinthProjectsResponse:
            """
            [
              {"id":"AANobbMI","slug":"sodium","project_type":"mod","title":"Sodium","description":"A high-performance rendering engine replacement for Minecraft.","icon_url":null,"downloads":175556776,"game_versions":["1.20.1"],"loaders":["fabric","neoforge","quilt","utility"]},
              {"id":"required-shader","slug":"required-shader","project_type":"shader","title":"Required Shader","description":"Shader","icon_url":null,"downloads":5,"game_versions":["1.20.1"],"loaders":[]}
            ]
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.GetProjectDependenciesAsync(new ResourceProjectDependenciesRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "main-mod"
        });

        var project = Assert.Single(result.RequiredProjects);
        Assert.Equal("AANobbMI", project.ProjectId);
        Assert.Equal("Sodium", project.Title);
        Assert.Equal(ResourceProjectKind.Mod, project.Kind);
        Assert.Equal(ResourceProjectSource.Modrinth, project.Source);
        Assert.Equal(["1.20.1"], project.SupportedMinecraftVersions);
        Assert.Equal(["fabric", "neoforge", "quilt"], project.SupportedLoaders);
        Assert.Equal("https://modrinth.com/mod/sodium", project.ProjectUrl);

        Assert.Equal(2, handler.Requests.Count);
        var versionsRequest = handler.Requests[0];
        Assert.Equal("api.modrinth.com", versionsRequest.RequestUri!.Host);
        Assert.Contains("/project/main-mod/version", versionsRequest.RequestUri.AbsolutePath);

        var projectsRequest = handler.Requests[1];
        Assert.Contains("/projects", projectsRequest.RequestUri!.AbsolutePath);
        Assert.Contains("AANobbMI", Uri.UnescapeDataString(projectsRequest.RequestUri.Query));
        Assert.DoesNotContain("optional-lib", Uri.UnescapeDataString(projectsRequest.RequestUri.Query));
    }

    [Fact]
    public async Task GetProjectDependenciesReturnsEmptyWhenModrinthVersionsHaveNoRequiredProjectDependencies()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            modrinthVersionResponse:
            """
            [{"id":"sodium-version","dependencies":[],"files":[{"filename":"sodium.jar","url":"https://downloads.example.test/sodium.jar","primary":true}]}]
            """,
            modrinthDependenciesResponse:
            """
            {"projects":[{"id":"reverse","slug":"reverse","project_type":"mod","title":"Reverse","description":"Depends on Sodium","downloads":1,"game_versions":["1.20.1"],"loaders":["fabric"]}],"versions":[]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.GetProjectDependenciesAsync(new ResourceProjectDependenciesRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "main-mod"
        });

        Assert.Empty(result.RequiredProjects);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("/project/main-mod/version", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetProjectDependenciesReturnsEmptyForUnsupportedSource()
    {
        var handler = new ResourceCatalogHandler("""{"hits":[]}""");
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.GetProjectDependenciesAsync(new ResourceProjectDependenciesRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234"
        });

        Assert.Empty(result.RequiredProjects);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetModpackVersionsQueriesCurseForgeWithMinecraftVersionAndLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":101,"displayName":"Pack 1.0","fileName":"pack.zip","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.20.1","Fabric"],"sortableGameVersions":[{"gameVersion":"1.20.1","modLoader":4}]}],"pagination":{"index":0,"pageSize":10000,"resultCount":1,"totalCount":1}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            PageSize = 50
        });

        var version = Assert.Single(result.Versions);
        Assert.Equal(ResourceProjectKind.Modpack, version.Kind);
        Assert.Equal(["fabric"], version.Loaders);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.curseforge.com", request.RequestUri!.Host);
        Assert.Contains("/mods/1234/files", request.RequestUri.AbsolutePath);
        Assert.Contains("gameVersion=1.20.1", request.RequestUri.Query);
        Assert.Contains("modLoaderType=4", request.RequestUri.Query);
    }

    [Fact]
    public async Task GetModpackVersionsKeepsCurseForgeFileWhenNameMissingAndDownloadUrlExists()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":101,"displayName":"Pack 1.0","fileName":"","downloadUrl":"https://downloads.example.test/modpack","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.20.1"],"sortableGameVersions":[{"gameVersion":"1.20.1","modLoader":4}]}],"pagination":{"index":0,"pageSize":10000,"resultCount":1,"totalCount":1}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = ResourceProjectKind.Modpack,
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            IncludeAllVersions = true
        });

        var version = Assert.Single(result.Versions);
        Assert.Equal(string.Empty, version.FileName);
        Assert.Equal("https://downloads.example.test/modpack", version.PrimaryDownloadUrl);
    }

    [Fact]
    public async Task SearchModsAddsCurseForgeCategoryIdWhenTypeFilterIsSelected()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"Tech Mod","slug":"tech-mod","summary":"Tech","downloadCount":120,"links":null,"logo":null}],"pagination":{"index":0,"pageSize":20,"resultCount":1,"totalCount":1}}
            """,
            curseForgeCategoriesResponse:
            """
            {"data":[{"id":4471,"name":"Technology","slug":"technology"},{"id":4472,"name":"Magic","slug":"magic"}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            Source = ResourceProjectSource.CurseForge,
            Category = ResourceProjectCategory.Technology
        });

        Assert.Single(result.Projects);
        Assert.Equal(2, handler.Requests.Count);
        var categoriesRequest = handler.Requests[0];
        Assert.Equal("api.curseforge.com", categoriesRequest.RequestUri!.Host);
        Assert.Contains("/categories", categoriesRequest.RequestUri.AbsolutePath);
        Assert.Contains("gameId=432", categoriesRequest.RequestUri.Query);
        Assert.Contains("classId=6", categoriesRequest.RequestUri.Query);
        Assert.True(categoriesRequest.Headers.TryGetValues("x-api-key", out var categoryHeaderValues));
        Assert.Equal("test-key", Assert.Single(categoryHeaderValues));

        var searchRequest = handler.Requests[1];
        Assert.Contains("/mods/search", searchRequest.RequestUri!.AbsolutePath);
        Assert.Contains("categoryId=4471", searchRequest.RequestUri.Query);
    }

    [Fact]
    public async Task SearchModsSkipsCurseForgeWhenSelectedCategoryCannotBeResolved()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":9,"name":"Magic Mod","slug":"magic-mod","summary":"Magic","downloadCount":120,"links":null,"logo":null}]}
            """,
            curseForgeCategoriesResponse:
            """
            {"data":[{"id":4471,"name":"Technology","slug":"technology"}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.SearchModsAsync(new ResourceCatalogSearchRequest
        {
            Source = ResourceProjectSource.CurseForge,
            Category = ResourceProjectCategory.Magic
        });

        Assert.Empty(result.Projects);
        Assert.Single(handler.Requests);
        Assert.Contains("/categories", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.AbsolutePath.Contains("/mods/search", StringComparison.Ordinal));
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
    public async Task GetProjectVersionsMapsRequiredModrinthVersionDependencies()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            modrinthVersionResponse:
            """
            [
              {"id":"v1","name":"Iris 1.0","version_number":"1.0.0","version_type":"release","date_published":"2024-01-02T00:00:00Z","downloads":15,"game_versions":["1.20.1"],"loaders":["fabric"],"dependencies":[
                {"project_id":"AANobbMI","version_id":"sodium-version","dependency_type":"required"},
                {"project_id":"optional-lib","dependency_type":"optional"},
                {"project_id":"shader-dependency","dependency_type":"required"},
                {"file_name":"external.jar","dependency_type":"required"},
                {"project_id":"iris","dependency_type":"required"}
              ],"files":[{"filename":"iris.jar","url":"https://downloads.example.test/iris.jar","primary":true}]},
              {"id":"v2","name":"Iris 2.0","version_number":"2.0.0","version_type":"release","date_published":"2024-01-03T00:00:00Z","downloads":12,"game_versions":["1.20.1"],"loaders":["fabric"],"dependencies":[
                {"project_id":"AANobbMI","dependency_type":"required"},
                {"project_id":"AANobbMI","dependency_type":"required"}
              ],"files":[{"filename":"iris2.jar","url":"https://downloads.example.test/iris2.jar","primary":true}]}
            ]
            """,
            modrinthProjectsResponse:
            """
            [
              {"id":"AANobbMI","slug":"sodium","project_type":"mod","title":"Sodium","description":"Rendering engine","icon_url":null,"downloads":100,"game_versions":["1.20.1"],"loaders":["fabric","utility"]},
              {"id":"shader-dependency","slug":"shader-dependency","project_type":"shader","title":"Shader Dependency","description":"Shader","icon_url":null,"downloads":1,"game_versions":["1.20.1"],"loaders":[]}
            ]
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "iris",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric
        });

        Assert.Equal(2, result.Versions.Count);
        var dependency = Assert.Single(result.Versions[0].RequiredDependencies);
        Assert.Equal("AANobbMI", dependency.Project.ProjectId);
        Assert.Equal("sodium", dependency.Project.Slug);
        Assert.Equal("Sodium", dependency.Project.Title);
        Assert.Equal("sodium-version", dependency.VersionId);
        Assert.Equal(["fabric"], dependency.Project.SupportedLoaders);
        var dependencyWithoutVersion = Assert.Single(result.Versions[1].RequiredDependencies);
        Assert.Equal("Sodium", dependencyWithoutVersion.Project.Title);
        Assert.Equal(string.Empty, dependencyWithoutVersion.VersionId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/project/iris/version", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("/projects", handler.Requests[1].RequestUri!.AbsolutePath);
        var projectsQuery = Uri.UnescapeDataString(handler.Requests[1].RequestUri!.Query);
        Assert.Contains("AANobbMI", projectsQuery);
        Assert.Contains("shader-dependency", projectsQuery);
        Assert.DoesNotContain("optional-lib", projectsQuery);
    }

    [Fact]
    public async Task GetProjectVersionsQueriesCurseForgeWithMinecraftVersionAndLoader()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":101,"displayName":"JourneyMap 1.0","fileName":"journeymap.jar","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.18.2","Fabric"],"sortableGameVersions":[{"gameVersion":"1.18.2","modLoader":4}]}],"pagination":{"index":0,"pageSize":10000,"resultCount":1,"totalCount":1}}
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
    public async Task GetProjectVersionsMapsCurseForgeRequiredDependencies()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            curseForgeResponse:
            """
            {"data":[{"id":101,"displayName":"Main Mod 1.0","fileName":"main.jar","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.20.1","Fabric"],"sortableGameVersions":[{"gameVersion":"1.20.1","modLoader":4}],"dependencies":[
              {"modId":222,"relationType":3},
              {"modId":333,"relationType":2},
              {"modId":444,"relationType":3},
              {"modId":1234,"relationType":3}
            ]}],"pagination":{"index":0,"pageSize":10000,"resultCount":1,"totalCount":1}}
            """,
            curseForgeModsResponse:
            """
            {"data":[
              {"id":222,"classId":6,"name":"Library Mod","slug":"library-mod","summary":"Library","downloadCount":20,"logo":{"thumbnailUrl":"https://example.test/library.png"},"links":{"websiteUrl":"https://www.curseforge.com/minecraft/mc-mods/library-mod"},"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":4}],"gameVersionLatestFiles":[]},
              {"id":444,"classId":12,"name":"Resource Pack","slug":"resource-pack","summary":"Not a mod","downloadCount":5,"latestFilesIndexes":[],"gameVersionLatestFiles":[]}
            ]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            PageSize = 50
        });

        var version = Assert.Single(result.Versions);
        var dependency = Assert.Single(version.RequiredDependencies);
        Assert.Equal("222", dependency.Project.ProjectId);
        Assert.Equal("library-mod", dependency.Project.Slug);
        Assert.Equal("Library Mod", dependency.Project.Title);
        Assert.Equal(ResourceProjectSource.CurseForge, dependency.Project.Source);
        Assert.Equal(string.Empty, dependency.VersionId);
        Assert.Equal(["1.20.1"], dependency.Project.SupportedMinecraftVersions);
        Assert.Equal(["fabric"], dependency.Project.SupportedLoaders);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("/mods/1234/files", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("/v1/mods", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetProjectDependenciesMapsCurseForgeRequiredDependencies()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            curseForgeResponse:
            """
            {"data":[
              {"id":101,"displayName":"Main Mod 1.0","fileName":"main.jar","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.20.1","Fabric"],"sortableGameVersions":[{"gameVersion":"1.20.1","modLoader":4}],"dependencies":[{"modId":222,"relationType":3}]},
              {"id":102,"displayName":"Main Mod 1.1","fileName":"main-2.jar","releaseType":1,"downloadCount":7,"fileDate":"2024-01-03T00:00:00Z","gameVersions":["1.20.1","Fabric"],"sortableGameVersions":[{"gameVersion":"1.20.1","modLoader":4}],"dependencies":[{"modId":222,"relationType":3}]}
            ],"pagination":{"index":0,"pageSize":10000,"resultCount":2,"totalCount":2}}
            """,
            curseForgeModsResponse:
            """
            {"data":[{"id":222,"classId":6,"name":"Library Mod","slug":"library-mod","summary":"Library","downloadCount":20,"latestFilesIndexes":[{"gameVersion":"1.20.1","modLoader":4}],"gameVersionLatestFiles":[]}]}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.GetProjectDependenciesAsync(new ResourceProjectDependenciesRequest
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            Slug = "main-mod"
        });

        var dependency = Assert.Single(result.RequiredProjects);
        Assert.Equal("222", dependency.ProjectId);
        Assert.Equal("Library Mod", dependency.Title);
        Assert.Equal(ResourceProjectSource.CurseForge, dependency.Source);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/mods/1234/files", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/mods", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetProjectVersionsQueriesCurseForgeWithOffsetAndMapsHasMore()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            """
            {"data":[{"id":101,"displayName":"JourneyMap 1.0","fileName":"journeymap.jar","releaseType":1,"downloadCount":9,"fileDate":"2024-01-02T00:00:00Z","gameVersions":["1.18.2","Fabric"],"sortableGameVersions":[{"gameVersion":"1.18.2","modLoader":4}]}],"pagination":{"index":50,"pageSize":25,"resultCount":1,"totalCount":100}}
            """);
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver("test-key"));

        var result = await service.GetProjectVersionsAsync(new ResourceProjectVersionsRequest
        {
            Source = ResourceProjectSource.CurseForge,
            ProjectId = "1234",
            IncludeAllVersions = true,
            Offset = 50,
            PageSize = 25
        });

        Assert.Single(result.Versions);
        Assert.True(result.HasMore);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.curseforge.com", request.RequestUri!.Host);
        Assert.Contains("index=50", request.RequestUri.Query);
        Assert.Contains("pageSize=25", request.RequestUri.Query);
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
            PageSize = 10000
        });

        Assert.Single(result.Versions);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.curseforge.com", request.RequestUri!.Host);
        Assert.Contains("/mods/1234/files", request.RequestUri.AbsolutePath);
        Assert.DoesNotContain("gameVersion=", request.RequestUri.Query);
        Assert.DoesNotContain("modLoaderType=", request.RequestUri.Query);
        Assert.Contains("pageSize=10000", request.RequestUri.Query);
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
    public async Task InstallProjectVersionDownloadsResourcePackToInstanceResourcePacksDirectory()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/resourcepack.zip"] = "zip-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-resourcepack-install-{Guid.NewGuid():N}");
        try
        {
            var installedPath = await service.InstallProjectVersionAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.ResourcePack,
                    VersionId = "version-1",
                    FileName = "resourcepack.zip",
                    PrimaryDownloadUrl = "https://downloads.example.test/resourcepack.zip"
                },
                new GameInstance
                {
                    Id = "instance",
                    InstanceDirectory = instanceDirectory
                });

            Assert.Equal(Path.Combine(instanceDirectory, "resourcepacks", "resourcepack.zip"), installedPath);
            Assert.True(File.Exists(installedPath));
            Assert.Equal("zip-content", await File.ReadAllTextAsync(installedPath));
        }
        finally
        {
            if (Directory.Exists(instanceDirectory))
                Directory.Delete(instanceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InstallProjectVersionDownloadsShaderPackToInstanceShaderPacksDirectory()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/shaderpack.zip"] = "zip-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-shaderpack-install-{Guid.NewGuid():N}");
        try
        {
            var installedPath = await service.InstallProjectVersionAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.ShaderPack,
                    VersionId = "version-1",
                    FileName = "shaderpack.zip",
                    PrimaryDownloadUrl = "https://downloads.example.test/shaderpack.zip"
                },
                new GameInstance
                {
                    Id = "instance",
                    InstanceDirectory = instanceDirectory
                });

            Assert.Equal(Path.Combine(instanceDirectory, "shaderpacks", "shaderpack.zip"), installedPath);
            Assert.True(File.Exists(installedPath));
            Assert.Equal("zip-content", await File.ReadAllTextAsync(installedPath));
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
    public async Task DownloadProjectVersionUsesZipDefaultForShaderPackWhenFileNameMissing()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/shader"] = "zip-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"launcher-shaderpack-download-{Guid.NewGuid():N}");
        try
        {
            var downloadedPath = await service.DownloadProjectVersionAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.ShaderPack,
                    VersionId = "version-1",
                    PrimaryDownloadUrl = "https://downloads.example.test/shader"
                },
                targetDirectory);

            Assert.Equal(Path.Combine(targetDirectory, "version-1.zip"), downloadedPath);
            Assert.True(File.Exists(downloadedPath));
            Assert.Equal("zip-content", await File.ReadAllTextAsync(downloadedPath));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadProjectVersionUsesZipDefaultForWorldWhenFileNameMissing()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/world"] = "zip-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"launcher-world-download-{Guid.NewGuid():N}");
        try
        {
            var downloadedPath = await service.DownloadProjectVersionAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.World,
                    VersionId = "version-1",
                    PrimaryDownloadUrl = "https://downloads.example.test/world"
                },
                targetDirectory);

            Assert.Equal(Path.Combine(targetDirectory, "version-1.zip"), downloadedPath);
            Assert.True(File.Exists(downloadedPath));
            Assert.Equal("zip-content", await File.ReadAllTextAsync(downloadedPath));
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadProjectVersionUsesMrpackDefaultForModpackWhenFileNameMissing()
    {
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            downloadResponses: new Dictionary<string, string>
            {
                ["https://downloads.example.test/modpack"] = "pack-content"
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"launcher-modpack-download-{Guid.NewGuid():N}");
        try
        {
            var downloadedPath = await service.DownloadProjectVersionAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Modpack,
                    VersionId = "version-1",
                    PrimaryDownloadUrl = "https://downloads.example.test/modpack"
                },
                targetDirectory);

            Assert.Equal(Path.Combine(targetDirectory, "version-1.mrpack"), downloadedPath);
            Assert.True(File.Exists(downloadedPath));
            Assert.Equal("pack-content", await File.ReadAllTextAsync(downloadedPath));
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

    [Fact]
    public async Task ProjectVersionInstallExistsReturnsTrueWhenInstanceResourcePackFileExists()
    {
        var handler = new ResourceCatalogHandler("""{"hits":[]}""");
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-resourcepack-install-exists-{Guid.NewGuid():N}");
        try
        {
            var resourcePacksDirectory = Path.Combine(instanceDirectory, "resourcepacks");
            Directory.CreateDirectory(resourcePacksDirectory);
            await File.WriteAllTextAsync(Path.Combine(resourcePacksDirectory, "pack.zip"), "existing");

            var exists = await service.ProjectVersionInstallExistsAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.ResourcePack,
                    VersionId = "version-1",
                    FileName = "pack.zip"
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

    [Fact]
    public async Task ProjectVersionInstallExistsReturnsTrueWhenInstanceShaderPackFileExists()
    {
        var handler = new ResourceCatalogHandler("""{"hits":[]}""");
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-shaderpack-install-exists-{Guid.NewGuid():N}");
        try
        {
            var shaderPacksDirectory = Path.Combine(instanceDirectory, "shaderpacks");
            Directory.CreateDirectory(shaderPacksDirectory);
            await File.WriteAllTextAsync(Path.Combine(shaderPacksDirectory, "pack.zip"), "existing");

            var exists = await service.ProjectVersionInstallExistsAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.ShaderPack,
                    VersionId = "version-1",
                    FileName = "pack.zip"
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

    [Fact]
    public async Task ProjectVersionInstallExistsReturnsFalseForWorldWhenSameSaveExists()
    {
        var handler = new ResourceCatalogHandler("""{"hits":[]}""");
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-world-install-exists-{Guid.NewGuid():N}");
        try
        {
            var savesDirectory = Path.Combine(instanceDirectory, "saves", "World");
            Directory.CreateDirectory(savesDirectory);
            await File.WriteAllTextAsync(Path.Combine(savesDirectory, "level.dat"), "existing");

            var exists = await service.ProjectVersionInstallExistsAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.World,
                    VersionId = "version-1",
                    FileName = "World.zip"
                },
                new GameInstance
                {
                    Id = "instance",
                    InstanceDirectory = instanceDirectory
                });

            Assert.False(exists);
        }
        finally
        {
            if (Directory.Exists(instanceDirectory))
                Directory.Delete(instanceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InstallProjectVersionImportsWorldArchiveToInstanceSavesDirectory()
    {
        var archiveBytes = CreateWorldArchiveBytes("SkyBlock");
        var handler = new ResourceCatalogHandler(
            """{"hits":[]}""",
            binaryDownloadResponses: new Dictionary<string, byte[]>
            {
                ["https://downloads.example.test/world.zip"] = archiveBytes
            });
        var service = new ResourceCatalogService(
            new HttpClient(handler),
            curseForgeApiKeyResolver: new StubCurseForgeApiKeyResolver(null));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), $"launcher-world-install-{Guid.NewGuid():N}");
        try
        {
            var installedPath = await service.InstallProjectVersionAsync(
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.World,
                    VersionId = "version-1",
                    FileName = "world.zip",
                    PrimaryDownloadUrl = "https://downloads.example.test/world.zip"
                },
                new GameInstance
                {
                    Id = "instance",
                    InstanceDirectory = instanceDirectory
                });

            Assert.Equal(Path.Combine(instanceDirectory, "saves", "SkyBlock"), installedPath);
            Assert.True(File.Exists(Path.Combine(installedPath, "level.dat")));
            Assert.True(File.Exists(Path.Combine(installedPath, "region", "r.0.0.mca")));
        }
        finally
        {
            if (Directory.Exists(instanceDirectory))
                Directory.Delete(instanceDirectory, recursive: true);
        }
    }

    private static byte[] CreateWorldArchiveBytes(string rootDirectoryName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var levelEntry = archive.CreateEntry($"{rootDirectoryName}/level.dat");
            using (var writer = new StreamWriter(levelEntry.Open()))
                writer.Write("level");

            var regionEntry = archive.CreateEntry($"{rootDirectoryName}/region/r.0.0.mca");
            using var regionWriter = new StreamWriter(regionEntry.Open());
            regionWriter.Write("region");
        }

        return stream.ToArray();
    }

    private sealed class ResourceCatalogHandler : HttpMessageHandler
    {
        private readonly string modrinthResponse;
        private readonly string curseForgeResponse;
        private readonly string curseForgeCategoriesResponse;
        private readonly string modrinthVersionResponse;
        private readonly string modrinthDependenciesResponse;
        private readonly string modrinthProjectsResponse;
        private readonly string curseForgeModsResponse;
        private readonly IReadOnlyDictionary<string, string> downloadResponses;
        private readonly IReadOnlyDictionary<string, byte[]> binaryDownloadResponses;

        public ResourceCatalogHandler(
            string modrinthResponse,
            string? curseForgeResponse = null,
            string? curseForgeCategoriesResponse = null,
            string? modrinthVersionResponse = null,
            string? modrinthDependenciesResponse = null,
            string? modrinthProjectsResponse = null,
            string? curseForgeModsResponse = null,
            IReadOnlyDictionary<string, string>? downloadResponses = null,
            IReadOnlyDictionary<string, byte[]>? binaryDownloadResponses = null)
        {
            this.modrinthResponse = modrinthResponse;
            this.curseForgeResponse = curseForgeResponse ?? """{"data":[]}""";
            this.curseForgeCategoriesResponse = curseForgeCategoriesResponse ?? """{"data":[]}""";
            this.modrinthVersionResponse = modrinthVersionResponse ?? "[]";
            this.modrinthDependenciesResponse = modrinthDependenciesResponse ?? """{"projects":[],"versions":[]}""";
            this.modrinthProjectsResponse = modrinthProjectsResponse ?? "[]";
            this.curseForgeModsResponse = curseForgeModsResponse ?? """{"data":[]}""";
            this.downloadResponses = downloadResponses ?? new Dictionary<string, string>();
            this.binaryDownloadResponses = binaryDownloadResponses ?? new Dictionary<string, byte[]>();
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

            if (binaryDownloadResponses.TryGetValue(request.RequestUri.ToString(), out var binaryDownloadBody))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(binaryDownloadBody)
                });
            }

            if (request.RequestUri.Host == "downloads.example.test")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var body = request.RequestUri?.Host switch
            {
                "api.modrinth.com" when string.Equals(request.RequestUri.AbsolutePath, "/v2/projects", StringComparison.Ordinal)
                    => modrinthProjectsResponse,
                "api.modrinth.com" when request.RequestUri.AbsolutePath.Contains("/dependencies", StringComparison.Ordinal)
                    => modrinthDependenciesResponse,
                "api.modrinth.com" when request.RequestUri.AbsolutePath.Contains("/version", StringComparison.Ordinal)
                    => modrinthVersionResponse,
                "api.modrinth.com" => modrinthResponse,
                "api.curseforge.com" when request.RequestUri.AbsolutePath.Contains("/categories", StringComparison.Ordinal)
                    => curseForgeCategoriesResponse,
                "api.curseforge.com" when request.Method == HttpMethod.Post
                    && string.Equals(request.RequestUri.AbsolutePath, "/v1/mods", StringComparison.Ordinal)
                    => curseForgeModsResponse,
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
