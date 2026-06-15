using Launcher.Domain.Models;
using Launcher.Infrastructure.Modrinth;

namespace Launcher.Tests.Infrastructure.Modrinth;

public sealed class ModrinthServiceTests
{
    [Fact]
    public async Task ModrinthSearchAddsMinecraftVersionAndLoaderFacets()
    {
        var handler = new CaptureHandler("""
            {"hits":[{"project_id":"p1","slug":"sodium","title":"Sodium","description":"Fast","icon_url":null,"downloads":42}]}
            """);
        var service = new ModrinthService(new HttpClient(handler));

        var results = await service.SearchModsAsync("sodium", "1.20.1", LoaderKind.Fabric);

        Assert.Single(results);
        Assert.Contains("query=sodium", handler.LastRequest!.Query);
        Assert.Contains("versions%3A1.20.1", handler.LastRequest.Query);
        Assert.Contains("categories%3Afabric", handler.LastRequest.Query);
    }

}

