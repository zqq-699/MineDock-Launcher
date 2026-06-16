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

    [Fact]
    public async Task ModrinthServiceInstallsFabricApiUsingFabricApiProjectSlug()
    {
        var handler = new SequencedHandler(
            """
            [{"version_number":"0.120.0+1.20.1","files":[{"filename":"fabric-api.jar","url":"https://cdn.modrinth.com/data/fabric-api.jar","primary":true}]}]
            """,
            "fabric api bytes");
        var service = new ModrinthService(new HttpClient(handler));
        var instanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        var instance = new GameInstance
        {
            Name = "Fabric Pack",
            MinecraftVersion = "1.20.1",
            Loader = LoaderKind.Fabric,
            InstanceDirectory = instanceDirectory
        };

        var path = await service.InstallFabricApiAsync(instance, null);

        Assert.Contains("/project/fabric-api/version", handler.RequestUris[0].AbsoluteUri);
        Assert.Contains("game_versions=%5B%221.20.1%22%5D", handler.RequestUris[0].Query);
        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(instanceDirectory, "mods", "fabric-api.jar"), path);
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;

        public SequencedHandler(string versionsResponseBody, string fileResponseBody)
        {
            responses = new Queue<HttpResponseMessage>(
            [
                CreateJsonResponse(versionsResponseBody),
                CreateBinaryResponse(fileResponseBody)
            ]);
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(responses.Dequeue());
        }

        private static HttpResponseMessage CreateJsonResponse(string body)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        }

        private static HttpResponseMessage CreateBinaryResponse(string body)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        }
    }
}

