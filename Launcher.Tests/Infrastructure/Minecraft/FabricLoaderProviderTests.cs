using System.Net;
using System.Net.Http;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class FabricLoaderProviderTests : TestTempDirectory
{
    [Fact]
    public async Task VanillaVersionComposerCreatesOnlyFinalVersionDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var finalVersionName = await VanillaVersionComposer.CreateFinalVersionAsync(
            new HttpClient(new VanillaInstallHandler()),
            "1.20.2",
            "1.20.2",
            minecraftDirectory);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var versionDirectories = Directory.GetDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal("1.20.2", finalVersionName);
        Assert.Equal(["1.20.2"], versionDirectories);
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.2", "1.20.2.json")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.2", "1.20.2.jar")));
    }

    [Fact]
    public async Task FabricLoaderProviderReturnsEmptyWhenFabricMetadataEndpointReturnsNotFound()
    {
        var provider = new FabricLoaderProvider(new HttpClient(new NotFoundHandler()));

        var versions = await provider.GetLoaderVersionsAsync("1.99.99");

        Assert.Empty(versions);
    }

    [Fact]
    public async Task FabricLoaderProviderReturnsEmptyWhenFabricMetadataEndpointReturnsBadRequest()
    {
        var provider = new FabricLoaderProvider(new HttpClient(new BadRequestHandler()));

        var versions = await provider.GetLoaderVersionsAsync("1.7.2");

        Assert.Empty(versions);
    }

    [Fact]
    public async Task FabricLoaderProviderLoadsSnapshotVersionsFromDirectMetadataEndpoint()
    {
        var provider = new FabricLoaderProvider(new HttpClient(new SnapshotLoaderHandler()));

        var versions = await provider.GetLoaderVersionsAsync("26w14a");

        var version = Assert.Single(versions);
        Assert.Equal("0.19.3", version.Version);
        Assert.True(version.IsStable);
    }

    [Theory]
    [InlineData("Cannot find any loader for 1.99.99")]
    [InlineData("No loader available for 1.99.99")]
    [InlineData("Response status code does not indicate success: 404 (Not Found).")]
    [InlineData("Could not find game version 1.99.99")]
    [InlineData("Unsupported game version: 1.99.99")]
    public void FabricLoaderProviderTreatsNoLoaderMessagesAsNoAvailableVersions(string message)
    {
        var exception = new InvalidOperationException(
            "wrapper",
            new Exception(message));

        var result = FabricLoaderProvider.IsNoAvailableVersionException(exception, "1.99.99");

        Assert.True(result);
    }

    [Fact]
    public void FabricLoaderProviderDoesNotTreatNetworkFailureAsNoAvailableVersion()
    {
        var exception = new HttpRequestException("connection reset");

        var result = FabricLoaderProvider.IsNoAvailableVersionException(exception, "1.99.99");

        Assert.False(result);
    }

    [Fact]
    public async Task FabricVersionComposerCreatesOnlyFinalVersionDirectory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var finalVersionName = await FabricVersionComposer.CreateFinalVersionAsync(
            new HttpClient(new FabricInstallHandler()),
            "1.20.2",
            "0.19.3",
            "1.20.2-fabric-0.19.3",
            minecraftDirectory);

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var versionDirectories = Directory.GetDirectories(versionsDirectory)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal("1.20.2-fabric-0.19.3", finalVersionName);
        Assert.Equal(["1.20.2-fabric-0.19.3"], versionDirectories);
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.2-fabric-0.19.3", "1.20.2-fabric-0.19.3.json")));
        Assert.True(File.Exists(Path.Combine(versionsDirectory, "1.20.2-fabric-0.19.3", "1.20.2-fabric-0.19.3.jar")));
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            });
        }
    }

    private sealed class BadRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = request
            });
        }
    }

    private sealed class SnapshotLoaderHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var absoluteUri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            var content = absoluteUri switch
            {
                "https://meta.fabricmc.net/v2/versions/loader/26w14a" => """
                    [
                      {
                        "loader": {
                          "separator": ".",
                          "build": 1,
                          "maven": "net.fabricmc:fabric-loader:0.19.3",
                          "version": "0.19.3",
                          "stable": true
                        }
                      }
                    ]
                    """,
                _ => throw new InvalidOperationException($"Unexpected request: {absoluteUri}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            });
        }
    }

    private sealed class VanillaInstallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsoluteUri switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => """
                    {
                      "versions": [
                        {
                          "id": "1.20.2",
                          "url": "https://example.test/mojang/1.20.2.json"
                        }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.json" => """
                    {
                      "id": "1.20.2",
                      "type": "release",
                      "downloads": {
                        "client": {
                          "url": "https://example.test/mojang/1.20.2.jar"
                        }
                      },
                      "libraries": [
                        { "name": "com.mojang:patchy:2.2.10" }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.jar" => null,
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            response.Content = content is null
                ? new ByteArrayContent("fake jar"u8.ToArray())
                : new StringContent(content);

            return Task.FromResult(response);
        }
    }

    private sealed class FabricInstallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsoluteUri switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => """
                    {
                      "versions": [
                        {
                          "id": "1.20.2",
                          "url": "https://example.test/mojang/1.20.2.json"
                        }
                      ]
                    }
                    """,
                "https://example.test/mojang/1.20.2.json" => """
                    {
                      "id": "1.20.2",
                      "type": "release",
                      "downloads": {
                        "client": {
                          "url": "https://example.test/mojang/1.20.2.jar"
                        }
                      },
                      "libraries": [
                        { "name": "com.mojang:patchy:2.2.10" }
                      ],
                      "arguments": {
                        "game": [ "--username", "${auth_player_name}" ],
                        "jvm": [ "-Djava.library.path=${natives_directory}" ]
                      }
                    }
                    """,
                "https://meta.fabricmc.net/v2/versions/loader/1.20.2/0.19.3/profile/json" => """
                    {
                      "id": "fabric-loader-0.19.3-1.20.2",
                      "inheritsFrom": "1.20.2",
                      "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
                      "libraries": [
                        { "name": "net.fabricmc:fabric-loader:0.19.3" }
                      ],
                      "arguments": {
                        "game": [ "--fabric", "1" ],
                        "jvm": [ "-Dfabric.skipMcProvider=true" ]
                      }
                    }
                    """,
                "https://example.test/mojang/1.20.2.jar" => null,
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            };

            response.Content = content is null
                ? new ByteArrayContent("fake jar"u8.ToArray())
                : new StringContent(content);

            return Task.FromResult(response);
        }
    }
}
